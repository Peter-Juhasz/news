using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using PeterJuhasz.Text.Html.Model;
using PeterJuhasz.Text.Html.Writer;

namespace News.Services;

/// <summary>
/// Resolves the addresses of a parsed page, and writes a piece of it back out with them made
/// absolute. A piece that leaves the page it came from has nothing left to resolve a relative
/// source or link against.
/// </summary>
public static class HtmlAddresses
{
    /// <summary>
    /// Writes HTML back out with its letters as they are. The default encoder turns everything
    /// outside Basic Latin into a character reference, which is every accented letter of a page
    /// not written in English.
    /// </summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// The address relative links resolve against: the page's own, unless it declares another
    /// with a &lt;base&gt; element.
    /// </summary>
    public static Uri ResolveBaseUri(HtmlDocument document, Uri requestUri)
    {
        var declared = document.GetElementsByTagName("base")
            .Select(static element => element.GetAttribute("href")?.Value)
            .FirstOrDefault(static href => href is not null);

        return Uri.TryCreate(requestUri, declared, out var baseUri) ? baseUri : requestUri;
    }

    /// <summary>
    /// An address made absolute against the page. Anything that does not lead to a web address —
    /// a script, a mail link, a picture carried inside the page — is no link to open and no picture
    /// to fetch.
    /// </summary>
    public static string? ResolveAddress(string? value, Uri baseUri) =>
        !string.IsNullOrWhiteSpace(value) &&
        Uri.TryCreate(baseUri, value.Trim(), out var address) &&
        address.Scheme is "http" or "https"
            ? address.AbsoluteUri
            : null;

    /// <summary>
    /// The element as HTML, its own tags included, with the addresses in it made absolute.
    /// </summary>
    public static string WriteWithAbsoluteAddresses(HtmlElement element, Uri baseUri)
    {
        var buffer = new ArrayBufferWriter<char>();
        new AbsoluteAddressWriter(HtmlWriter.Create(buffer, Encoder), baseUri).VisitElement(element);
        return buffer.WrittenSpan.ToString();
    }

    private sealed class AbsoluteAddressWriter(HtmlWriter<ArrayBufferWriter<char>> writer, Uri baseUri)
        : HtmlWriterVisitor<ArrayBufferWriter<char>>(writer)
    {
        public override void VisitAttribute(HtmlAttribute attribute)
        {
            // anything that does not resolve to a web address — a data URI, a mailto link — is
            // written as it stands, since making it absolute would say nothing more than it does
            if (attribute.Name.ToLowerInvariant() is "href" or "src" &&
                ResolveAddress(attribute.Value, baseUri) is { } address)
            {
                Writer.WriteAttribute(attribute.Name, address);
                return;
            }

            base.VisitAttribute(attribute);
        }
    }
}
