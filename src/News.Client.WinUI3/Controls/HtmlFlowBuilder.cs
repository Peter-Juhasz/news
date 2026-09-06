using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.UI.Text;

namespace News.Client.WinUI3.Controls;

/// <summary>
/// Walks an HTML DOM and translates its main structure into <see cref="RichTextBlock"/> blocks:
/// block elements become paragraphs, inline formatting becomes run properties, images become
/// inline UI containers. Elements without a translation are emitted as their outer HTML text.
/// </summary>
internal sealed partial class HtmlFlowBuilder
{
    private const double ParagraphSpacing = 8;
    private const double HeadingSpacing = 12;
    private const double ListIndent = 20;
    private const double QuoteIndent = 16;

    private static readonly HtmlParser Parser = new();
    private static readonly FontFamily MonospaceFontFamily = new("Consolas");

    /// <summary>Non-content elements dropped together with their children.</summary>
    private static readonly HashSet<string> IgnoredElements = new(StringComparer.Ordinal)
    {
        "script", "style", "head", "title", "meta", "link", "base", "template", "noscript",
        "source", "track", "param", "wbr"
    };

    /// <summary>Elements that only break the flow into paragraphs around their children.</summary>
    private static readonly HashSet<string> BlockContainerElements = new(StringComparer.Ordinal)
    {
        "html", "body", "div", "section", "article", "header", "footer", "main", "aside", "nav",
        "figure", "figcaption", "address", "center", "details", "summary", "dl", "form", "fieldset",
        "table", "thead", "tbody", "tfoot", "tr", "caption"
    };

    /// <summary>Elements that pass their children through unchanged.</summary>
    private static readonly HashSet<string> InlineContainerElements = new(StringComparer.Ordinal)
    {
        "span", "font", "abbr", "acronym", "q", "time", "label", "mark", "bdi", "bdo", "data",
        "picture", "nobr", "ruby", "rt", "rp"
    };

    private readonly record struct TextFormat(
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        bool Monospace,
        bool Preformatted,
        double FontScale,
        Uri? Link)
    {
        // A struct's parameterless constructor zeroes every field, so the scale is set explicitly.
        public static TextFormat Default { get; } = new(
            Bold: false,
            Italic: false,
            Underline: false,
            Strikethrough: false,
            Monospace: false,
            Preformatted: false,
            FontScale: 1,
            Link: null);
    }

    private sealed class ListState(bool ordered, int start)
    {
        public bool Ordered { get; } = ordered;

        public int Counter { get; set; } = start;
    }

    private readonly List<Block> _blocks = [];
    private readonly List<Action<double>> _widthAppliers = [];
    private readonly Stack<ListState> _lists = new();
    private readonly double _baseFontSize;
    private readonly Action<Uri> _linkActivated;

    private Paragraph? _paragraph;
    private Hyperlink? _hyperlink;
    private Uri? _hyperlinkTarget;
    private Run? _lastRun;
    private bool _trimLeadingSpace = true;
    private double _indent;

    private HtmlFlowBuilder(double baseFontSize, Action<Uri> linkActivated)
    {
        _baseFontSize = baseFontSize;
        _linkActivated = linkActivated;
    }

    /// <param name="linkActivated">Called when a link is clicked; the builder never navigates itself.</param>
    public static HtmlFlowResult Build(string html, double baseFontSize, Action<Uri> linkActivated)
    {
        var builder = new HtmlFlowBuilder(baseFontSize, linkActivated);
        var document = Parser.ParseDocument(html);
        INode? root = document.Body ?? document.DocumentElement;

        if (root is not null)
        {
            builder.VisitChildren(root, TextFormat.Default);
        }

        builder.EndParagraph();
        return new HtmlFlowResult(builder._blocks, builder._widthAppliers);
    }

    [GeneratedRegex("[ \t\r\n\f]+")]
    private static partial Regex HtmlWhitespaceRegex();

    private void VisitChildren(INode node, TextFormat format)
    {
        foreach (var child in node.ChildNodes)
        {
            Visit(child, format);
        }
    }

    private void Visit(INode node, TextFormat format)
    {
        switch (node)
        {
            case IText text:
                AppendText(text.Data, format);
                break;

            case IElement element:
                VisitElement(element, format);
                break;

            // Comments, doctype and processing instructions carry no visible content.
        }
    }

    private void VisitElement(IElement element, TextFormat format)
    {
        switch (element.LocalName)
        {
            case "p":
                VisitBlock(element, format, topSpacing: 0);
                break;

            case "h1":
                VisitBlock(element, format with { Bold = true, FontScale = 1.5 }, HeadingSpacing);
                break;

            case "h2":
                VisitBlock(element, format with { Bold = true, FontScale = 1.3 }, HeadingSpacing);
                break;

            case "h3":
                VisitBlock(element, format with { Bold = true, FontScale = 1.15 }, HeadingSpacing);
                break;

            case "h4":
            case "h5":
            case "h6":
            case "dt":
                VisitBlock(element, format with { Bold = true }, topSpacing: 0);
                break;

            case "td":
                VisitTableCell(element, format);
                break;

            case "th":
                VisitTableCell(element, format with { Bold = true });
                break;

            case "blockquote":
                VisitBlock(element, format, topSpacing: 0, extraIndent: QuoteIndent);
                break;

            case "dd":
                VisitBlock(element, format, topSpacing: 0, extraIndent: ListIndent);
                break;

            case "pre":
                VisitBlock(element, format with { Monospace = true, Preformatted = true }, topSpacing: 0);
                break;

            case "ul":
            case "ol":
                VisitList(element, format);
                break;

            case "li":
                VisitListItem(element, format);
                break;

            case "br":
                AppendLineBreak();
                break;

            case "hr":
                AppendSeparator();
                break;

            case "img":
                AppendImage(element, format);
                break;

            case "a":
                VisitChildren(element, format with { Link = ResolveLink(element.GetAttribute("href")) ?? format.Link });
                break;

            case "b":
            case "strong":
                VisitChildren(element, format with { Bold = true });
                break;

            case "i":
            case "em":
            case "cite":
            case "dfn":
            case "var":
                VisitChildren(element, format with { Italic = true });
                break;

            case "u":
            case "ins":
                VisitChildren(element, format with { Underline = true });
                break;

            case "s":
            case "strike":
            case "del":
                VisitChildren(element, format with { Strikethrough = true });
                break;

            case "code":
            case "kbd":
            case "samp":
            case "tt":
                VisitChildren(element, format with { Monospace = true });
                break;

            case "small":
                VisitChildren(element, format with { FontScale = format.FontScale * 0.85 });
                break;

            case "big":
                VisitChildren(element, format with { FontScale = format.FontScale * 1.2 });
                break;

            case "sup":
            case "sub":
                VisitChildren(element, format with { FontScale = format.FontScale * 0.75 });
                break;

            default:
                VisitOtherElement(element, format);
                break;
        }
    }

    private void VisitOtherElement(IElement element, TextFormat format)
    {
        var name = element.LocalName;

        if (IgnoredElements.Contains(name))
        {
            return;
        }

        if (BlockContainerElements.Contains(name))
        {
            EndParagraph();
            VisitChildren(element, format);
            EndParagraph();
            return;
        }

        if (InlineContainerElements.Contains(name))
        {
            VisitChildren(element, format);
            return;
        }

        // No translation for this element: show its markup verbatim so nothing is lost.
        AppendText(element.OuterHtml, format);
    }

    private void VisitBlock(IElement element, TextFormat format, double topSpacing, double extraIndent = 0)
    {
        EndParagraph();
        _indent += extraIndent;
        StartParagraph(topSpacing);
        VisitChildren(element, format);
        EndParagraph();
        _indent -= extraIndent;
    }

    private void VisitList(IElement element, TextFormat format)
    {
        EndParagraph();

        var start = int.TryParse(element.GetAttribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStart)
            ? parsedStart
            : 1;

        _lists.Push(new ListState(element.LocalName == "ol", start));
        _indent += ListIndent;
        VisitChildren(element, format);
        _indent -= ListIndent;
        _lists.Pop();

        EndParagraph();
    }

    private void VisitListItem(IElement element, TextFormat format)
    {
        EndParagraph();
        StartParagraph(topSpacing: 0);

        var marker = _lists.TryPeek(out var list) && list.Ordered
            ? $"{list.Counter++}. "
            : "• ";

        AddRun(marker, format);
        VisitChildren(element, format);
        EndParagraph();
    }

    private void VisitTableCell(IElement element, TextFormat format)
    {
        // Cells of a row share one paragraph, separated by tabs; the row itself breaks the flow.
        VisitChildren(element, format);

        if (_paragraph is not null)
        {
            AddRun("\t", format);
        }
    }

    private void AppendText(string text, TextFormat format)
    {
        if (format.Preformatted)
        {
            var verbatim = text.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (verbatim.Length > 0)
            {
                AddRun(verbatim, format);
            }

            return;
        }

        var collapsed = HtmlWhitespaceRegex().Replace(text, " ");
        if (collapsed.Length == 0)
        {
            return;
        }

        if (collapsed == " ")
        {
            // Whitespace between elements only matters as a single separator inside a paragraph
            // that already has content and does not already end with a space.
            if (_paragraph is null || _trimLeadingSpace)
            {
                return;
            }

            AddRun(" ", format);
            return;
        }

        if (_paragraph is null || _trimLeadingSpace)
        {
            collapsed = collapsed.TrimStart(' ');
        }

        AddRun(collapsed, format);
    }

    private void AddRun(string text, TextFormat format)
    {
        var run = new Run { Text = text };

        if (format.Bold)
        {
            run.FontWeight = FontWeights.SemiBold;
        }

        if (format.Italic)
        {
            run.FontStyle = FontStyle.Italic;
        }

        var decorations = TextDecorations.None;
        if (format.Underline)
        {
            decorations |= TextDecorations.Underline;
        }

        if (format.Strikethrough)
        {
            decorations |= TextDecorations.Strikethrough;
        }

        if (decorations != TextDecorations.None)
        {
            run.TextDecorations = decorations;
        }

        if (format.Monospace)
        {
            run.FontFamily = MonospaceFontFamily;
        }

        if (format.FontScale != 1)
        {
            run.FontSize = _baseFontSize * format.FontScale;
        }

        GetInlines(format).Add(run);
        _lastRun = run;
        _trimLeadingSpace = char.IsWhiteSpace(text[^1]);
    }

    private void AppendLineBreak()
    {
        // A line break always sits directly in the paragraph; a Hyperlink cannot host it.
        EnsureParagraph().Inlines.Add(new LineBreak());
        _hyperlink = null;
        _hyperlinkTarget = null;
        _lastRun = null;
        _trimLeadingSpace = true;
    }

    private void AppendSeparator()
    {
        EndParagraph();

        var line = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            Background = Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var brush) && brush is Brush dividerBrush
                ? dividerBrush
                : new SolidColorBrush(Microsoft.UI.Colors.Gray)
        };

        var paragraph = StartParagraph(topSpacing: 0);
        paragraph.Inlines.Add(new InlineUIContainer { Child = line });
        _widthAppliers.Add(width => line.Width = width);
        _lastRun = null;

        EndParagraph();
    }

    private void AppendImage(IElement element, TextFormat format)
    {
        var source = element.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(source))
        {
            source = element.GetAttribute("data-src");
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            var alt = element.GetAttribute("alt");
            if (!string.IsNullOrWhiteSpace(alt))
            {
                AppendText(alt, format);
            }

            return;
        }

        if (IsTrackingPixel(element))
        {
            return;
        }

        var image = new Image
        {
            Source = new BitmapImage(uri),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left
        };

        // Images sit directly in the paragraph: an InlineUIContainer is not allowed inside a Hyperlink.
        EnsureParagraph().Inlines.Add(new InlineUIContainer { Child = image });
        _widthAppliers.Add(width => image.MaxWidth = width);
        _hyperlink = null;
        _hyperlinkTarget = null;
        _lastRun = null;
        _trimLeadingSpace = false;
    }

    private static bool IsTrackingPixel(IElement element) =>
        int.TryParse(element.GetAttribute("width"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
        int.TryParse(element.GetAttribute("height"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) &&
        width <= 1 && height <= 1;

    /// <summary>
    /// Only https addresses become links: feed content is untrusted, so file, javascript, data and
    /// every other scheme is left as plain text rather than handed to the shell.
    /// </summary>
    private static Uri? ResolveLink(string? href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;

    private InlineCollection GetInlines(TextFormat format)
    {
        var paragraph = EnsureParagraph();

        if (format.Link is null)
        {
            _hyperlink = null;
            _hyperlinkTarget = null;
            return paragraph.Inlines;
        }

        if (_hyperlink is null || _hyperlinkTarget != format.Link)
        {
            var target = format.Link;

            // NavigateUri is left unset on purpose: it would open the browser before the
            // confirmation is shown, and a Click handler cannot cancel that navigation.
            _hyperlink = new Hyperlink();
            _hyperlink.Click += (_, _) => _linkActivated(target);
            _hyperlinkTarget = target;
            paragraph.Inlines.Add(_hyperlink);
        }

        return _hyperlink.Inlines;
    }

    private Paragraph EnsureParagraph() => _paragraph ?? StartParagraph(topSpacing: 0);

    private Paragraph StartParagraph(double topSpacing)
    {
        EndParagraph();

        _paragraph = new Paragraph
        {
            Margin = new Thickness(_indent, topSpacing, 0, ParagraphSpacing)
        };

        _trimLeadingSpace = true;
        return _paragraph;
    }

    private void EndParagraph()
    {
        if (_paragraph is null)
        {
            return;
        }

        if (_lastRun is not null)
        {
            _lastRun.Text = _lastRun.Text.TrimEnd();
        }

        if (_paragraph.Inlines.Count > 0)
        {
            _blocks.Add(_paragraph);
        }

        _paragraph = null;
        _hyperlink = null;
        _hyperlinkTarget = null;
        _lastRun = null;
        _trimLeadingSpace = true;
    }
}

/// <summary>
/// Blocks produced from an HTML fragment, plus callbacks that size width-dependent elements
/// (images, separators) once the hosting control knows its width.
/// </summary>
internal sealed record HtmlFlowResult(IReadOnlyList<Block> Blocks, IReadOnlyList<Action<double>> WidthAppliers);
