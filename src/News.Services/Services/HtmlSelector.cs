using PeterJuhasz.Text.Html.Model;

namespace News.Services;

/// <summary>
/// Runs a selector against a parsed page. The parser matches one compound selector at a time — an
/// element name or <c>*</c>, then classes, IDs and exact attribute values — so the descendant
/// combinator is added here: each compound is looked for inside what the one before it matched.
/// </summary>
/// <remarks>
/// That covers what a source is likely to be written with, such as <c>ul.news li</c>. Child and
/// sibling combinators, selector lists and pseudo-classes are not supported, and are turned away
/// when the groups file is read rather than when a page is.
/// </remarks>
internal static class HtmlSelector
{
    private static readonly HtmlDocument EmptyDocument = HtmlDocument.Parse(string.Empty);

    /// <summary>
    /// Whether every compound of the selector is one the parser takes. It parses a selector before
    /// looking for anything, so a document with nothing in it is enough to find out.
    /// </summary>
    public static bool IsSupported(string selector)
    {
        var compounds = SplitCompounds(selector);

        if (compounds.Count == 0)
        {
            return false;
        }

        try
        {
            foreach (var compound in compounds)
            {
                EmptyDocument.QuerySelectorAll(compound);
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static IEnumerable<HtmlElement> SelectAll(HtmlDocument document, string selector) =>
        SplitCompounds(selector) is [var first, .. var rest]
            ? Descend(document.QuerySelectorAll(first), rest)
            : [];

    /// <summary>
    /// The first element inside <paramref name="element"/> the selector matches, as
    /// <c>querySelector</c> finds it: among its descendants, never the element itself.
    /// </summary>
    public static HtmlElement? Select(HtmlElement element, string selector) =>
        SplitCompounds(selector) is [var first, .. var rest]
            ? Descend(element.QuerySelectorAll(first), rest).FirstOrDefault()
            : null;

    private static IEnumerable<HtmlElement> Descend(IEnumerable<HtmlElement> matches, IEnumerable<string> compounds)
    {
        foreach (var compound in compounds)
        {
            // an element inside two nested matches is found from both, and kept where it was found
            // first, which leaves the matches in document order
            matches = matches.SelectMany(match => match.QuerySelectorAll(compound)).Distinct();
        }

        return matches;
    }

    /// <summary>
    /// Splits a selector at the whitespace between its compounds. Whitespace inside the brackets
    /// of an attribute selector belongs to its value rather than separating anything.
    /// </summary>
    private static List<string> SplitCompounds(string selector)
    {
        List<string> compounds = [];
        var start = 0;
        var inBrackets = false;

        for (var index = 0; index <= selector.Length; index++)
        {
            if (index < selector.Length)
            {
                var character = selector[index];

                if (character is '[' or ']')
                {
                    inBrackets = character is '[';
                    continue;
                }

                if (inBrackets || !char.IsWhiteSpace(character))
                {
                    continue;
                }
            }

            if (index > start)
            {
                compounds.Add(selector[start..index]);
            }

            start = index + 1;
        }

        return compounds;
    }
}
