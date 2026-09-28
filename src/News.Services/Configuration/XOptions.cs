namespace News.Configuration;

/// <summary>
/// The X app the X sources read as. Its key and secret are traded for the app's bearer token,
/// which reads what any signed-out visitor could.
/// </summary>
/// <remarks>
/// Secrets, so better kept in the environment or the .env file than in appsettings.json, which
/// is checked in: <c>X__ApiKey</c> and <c>X__ApiSecret</c>.
/// </remarks>
public sealed class XOptions
{
    public const string SectionName = "X";

    /// <summary>
    /// The app's API key, which X also calls its consumer key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The app's API key secret, which X also calls its consumer secret.
    /// </summary>
    public string? ApiSecret { get; set; }
}
