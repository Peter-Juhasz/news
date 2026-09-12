using Microsoft.UI.Xaml;

namespace News.Client.WinUI3.Configuration;

/// <summary>
/// How the app looks, bound from the "Appearance" configuration section.
/// </summary>
public sealed class AppearanceOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Appearance";

    /// <summary>
    /// <see cref="ElementTheme.Light"/> or <see cref="ElementTheme.Dark"/> to fix the app's colors,
    /// or <see cref="ElementTheme.Default"/> to follow whatever Windows itself is set to.
    /// </summary>
    public ElementTheme Theme { get; set; } = ElementTheme.Default;
}
