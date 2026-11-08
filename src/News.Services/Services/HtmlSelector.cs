using PeterJuhasz.Text.Html.Model;

namespace News.Services;

/// <summary>
/// Runs a selector against a parsed page. The parser takes CSS selectors as a whole: selector
/// lists, the descendant, child and next-sibling combinators, attribute matchers and the common
/// pseudo-classes.
/// </summary>
/// <remarks>
/// A selector the parser cannot read is turned away when the groups file is read rather than when
/// a page is.
/// </remarks>
public static class HtmlSelector
{
    private static readonly HtmlDocument EmptyDocument = HtmlDocument.Parse(string.Empty);

    /// <summary>
    /// Whether the parser takes the selector. It parses a selector before looking for anything, so
    /// a document with nothing in it is enough to find out.
    /// </summary>
    public static bool IsSupported(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        try
        {
            EmptyDocument.QuerySelectorAll(selector);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static IEnumerable<HtmlElement> SelectAll(HtmlDocument document, string selector) =>
        document.QuerySelectorAll(selector);

    /// <summary>
    /// The first element inside <paramref name="element"/> the selector matches, as
    /// <c>querySelector</c> finds it: among its descendants, never the element itself. As in the
    /// DOM, the whole selector is matched against the page, so in <c>div h2</c> the div may also
    /// be outside the element.
    /// </summary>
    public static HtmlElement? Select(HtmlElement element, string selector) =>
        element.QuerySelector(selector);
}
