using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using News.Configuration;

namespace News.Services;

/// <summary>
/// Signs requests to X's API with the app's bearer token, which it trades the app's key and secret
/// for the first time one is needed: OAuth 2.0's client credentials grant, as X's application-only
/// authentication has it.
/// </summary>
/// <remarks>
/// MSAL, Microsoft's library for the grant, finds a provider's token endpoint other than Entra ID's
/// through its OpenID Connect discovery document, which X does not publish; and X's token comes
/// without the expiry such a library plans its renewals by. The grant is a single request, made
/// through the handler's own pipeline so it is given the same patience with a failing server as
/// the request it is made for.
/// </remarks>
public sealed partial class XAuthenticationHandler(XAccessTokenCache cache, IOptions<XOptions> options) : DelegatingHandler
{
    private const string TokenUrl = "https://api.x.com/oauth2/token";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await cache.GetAsync(RequestTokenAsync, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // The token does not expire, so one turned away was revoked, or the app's key regenerated.
        // The request is tried once more with a new one, and whatever that returns is the answer.
        response.Dispose();
        cache.Invalidate(token);

        token = await cache.GetAsync(RequestTokenAsync, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var (apiKey, apiSecret) = (options.Value.ApiKey?.Trim(), options.Value.ApiSecret?.Trim());

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            throw new HttpRequestException(
                $"X's API cannot be read without the app's credentials. Set {XOptions.SectionName}:{nameof(XOptions.ApiKey)} " +
                $"and {XOptions.SectionName}:{nameof(XOptions.ApiSecret)}, as in the environment variables " +
                $"{XOptions.SectionName}__{nameof(XOptions.ApiKey)} and {XOptions.SectionName}__{nameof(XOptions.ApiSecret)}.");
        }

        // Each URL-encoded before they are joined, as OAuth has it. X issues them with nothing in
        // them to encode, but one pasted with a stray character in it would otherwise move the colon
        // that tells them apart.
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(apiKey)}:{Uri.EscapeDataString(apiSecret)}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")])
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await base.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync(XAuthenticationJsonContext.Default.TokenResponse, cancellationToken);

        return token is { AccessToken: { Length: > 0 } accessToken } &&
               string.Equals(token.TokenType, "bearer", StringComparison.OrdinalIgnoreCase)
            ? accessToken
            : throw new FormatException("X answered the request for the app's token with no bearer token.");
    }

    private sealed record TokenResponse(string? TokenType, string? AccessToken);

    /// <summary>
    /// Generated at build time rather than reflected over at run time, which a natively compiled
    /// app has nothing to reflect with.
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(TokenResponse))]
    private sealed partial class XAuthenticationJsonContext : JsonSerializerContext;
}
