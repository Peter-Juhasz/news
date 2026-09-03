using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace News.Client.WinUI3.Controls;

/// <summary>
/// Marks where a <see cref="TextBlock"/>'s text matches a search query, using the text layer's
/// own highlighting rather than a split into runs — so trimming, wrapping and line limits keep
/// working as written, and a recycled list row pays for one small object instead of a rebuilt
/// inline collection.
/// </summary>
public static class SearchHighlight
{
    /// <summary>
    /// Query to mark occurrences of. Null or empty leaves the text unmarked.
    /// </summary>
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query",
        typeof(string),
        typeof(SearchHighlight),
        new PropertyMetadata(null, static (sender, args) => OnQueryChanged((TextBlock)sender, args)));

    /// <summary>
    /// Whether this text block's text is already being watched, so a query that comes and goes
    /// does not stack up callbacks for it.
    /// </summary>
    private static readonly DependencyProperty IsWatchingTextProperty = DependencyProperty.RegisterAttached(
        "IsWatchingText",
        typeof(bool),
        typeof(SearchHighlight),
        new PropertyMetadata(false));

    public static string? GetQuery(TextBlock element) => (string?)element.GetValue(QueryProperty);

    public static void SetQuery(TextBlock element, string? value) => element.SetValue(QueryProperty, value);

    private static void OnQueryChanged(TextBlock textBlock, DependencyPropertyChangedEventArgs args)
    {
        // A recycled container takes its new text without the query changing with it, so the
        // marks follow the text as well as the query they are drawn for. Nothing unregisters:
        // the callback holds nothing but the text block it is registered on.
        if (args.NewValue is not null && !(bool)textBlock.GetValue(IsWatchingTextProperty))
        {
            textBlock.SetValue(IsWatchingTextProperty, true);
            textBlock.RegisterPropertyChangedCallback(
                TextBlock.TextProperty,
                static (sender, _) => Apply((TextBlock)sender));
        }

        Apply(textBlock);
    }

    private static void Apply(TextBlock textBlock)
    {
        // Editing an attached highlighter's ranges does not reliably repaint; replacing the
        // highlighter does.
        textBlock.TextHighlighters.Clear();

        var query = GetQuery(textBlock);
        var text = textBlock.Text;

        if (string.IsNullOrEmpty(query) || text.Length == 0)
        {
            return;
        }

        // Looked up per call rather than cached, so a theme switch reaches the next redraw.
        var highlighter = new TextHighlighter
        {
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
        };

        AddRanges(highlighter, text, query);

        if (highlighter.Ranges.Count > 0)
        {
            textBlock.TextHighlighters.Add(highlighter);
        }
    }

    /// <summary>
    /// Adds a range for every occurrence of <paramref name="query"/>, found the way the list's
    /// filter finds them — by the current culture, ignoring case. A culture-aware match is not
    /// always as long as the query that matched it, so each range takes its length from the
    /// match rather than from the query.
    /// </summary>
    private static void AddRanges(TextHighlighter highlighter, string text, string query)
    {
        var compareInfo = CultureInfo.CurrentCulture.CompareInfo;
        var offset = 0;

        while (offset < text.Length)
        {
            var index = compareInfo.IndexOf(
                text.AsSpan(offset),
                query,
                CompareOptions.IgnoreCase,
                out var matchLength);

            if (index < 0)
            {
                return;
            }

            highlighter.Ranges.Add(new TextRange
            {
                StartIndex = offset + index,
                Length = matchLength
            });

            // A match of no length would leave the search where it started.
            offset += index + Math.Max(matchLength, 1);
        }
    }
}
