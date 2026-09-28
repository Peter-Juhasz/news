namespace News.Services;

/// <summary>
/// The X app's bearer token, kept for as long as the app runs. X issues an app one token at a time
/// and gives it no expiry, so it is asked for once and shared by every request signed with it.
/// </summary>
/// <remarks>
/// Registered once for the whole app, where the handlers using it are not: the HTTP client factory
/// builds a client's handlers anew each time it renews its pipeline.
/// </remarks>
public sealed class XAccessTokenCache : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    /// <summary>
    /// The token, asked for with <paramref name="requestToken"/> where there is none yet. Requests
    /// arriving while it is being asked for wait for it, rather than each asking for one of its own.
    /// </summary>
    public async Task<string> GetAsync(
        Func<CancellationToken, Task<string>> requestToken,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _token) is { } token)
        {
            return token;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _token ??= await requestToken(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Forgets <paramref name="token"/>, which X turned away — unless another request has already
    /// replaced it, in which case the newer one is kept.
    /// </summary>
    public void Invalidate(string token) => Interlocked.CompareExchange(ref _token, null, token);

    public void Dispose() => _gate.Dispose();
}
