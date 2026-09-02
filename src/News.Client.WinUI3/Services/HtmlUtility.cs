using System.Net;
using System.Text.RegularExpressions;

namespace News.Client.WinUI3.Services;

public static partial class HtmlUtility
{
    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultiWhitespaceRegex();

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
