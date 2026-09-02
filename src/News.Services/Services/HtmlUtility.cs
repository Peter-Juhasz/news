using System.Net;
using System.Text.RegularExpressions;

namespace News.Services;

public static partial class HtmlUtility
{
    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex(
        """<img\b[^>]*?\bsrc\s*=\s*(?:"(?<url>[^"]*)"|'(?<url>[^']*)'|(?<url>[^\s"'>]+))""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ImageSourceRegex();

    /// <summary>
    /// The first usable &lt;img&gt; source in an HTML body, for items whose feed carries no
    /// enclosure or Media RSS image of its own.
    /// </summary>
    public static string? FindFirstImageUrl(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (Match match in ImageSourceRegex().Matches(content))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();

            if (IsAbsoluteHttpUrl(url))
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>
    /// Relative sources, data URIs and the like cannot be resolved without the article's base
    /// address, so they do not count as the item's image.
    /// </summary>
    private static bool IsAbsoluteHttpUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static string ToPlainText(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex().Replace(content, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return MultiWhitespaceRegex().Replace(decoded, " ").Trim();
    }

    public static bool LooksLikeHtml(string? content) =>
        !string.IsNullOrWhiteSpace(content) &&
        content.Contains('<', StringComparison.Ordinal) &&
        content.Contains('>', StringComparison.Ordinal);

    public static string BuildLead(string? content, int maxLength = 220)
    {
        var plainText = ToPlainText(content);
        if (plainText.Length <= maxLength)
        {
            return plainText;
        }

        return $"{plainText[..(maxLength - 1)].TrimEnd()}…";
    }
}
