using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PeterJuhasz.Text.Html.Model;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.UI.Text;

namespace News.Client.WinUI3.Controls;

/// <summary>
/// Walks an HTML DOM and translates its main structure into <see cref="RichTextBlock"/> blocks:
/// block elements become paragraphs, inline formatting becomes run properties, images become
/// inline UI containers. Elements without a translation are emitted as their outer HTML text.
/// </summary>
internal sealed partial class HtmlFlowBuilder : HtmlVisitor
{
    private const double ParagraphSpacing = 8;
    private const double HeadingSpacing = 12;
    private const double ListIndent = 20;
    private const double QuoteIndent = 16;

    private static readonly FontFamily MonospaceFontFamily = new("Consolas");

    /// <summary>Non-content elements dropped together with their children.</summary>
    private static readonly FrozenSet<string> IgnoredElements = new string[]
    {
        "script", "style", "head", "title", "meta", "link", "base", "template", "noscript",
        "source", "track", "param", "wbr"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Elements that only break the flow into paragraphs around their children.</summary>
    private static readonly FrozenSet<string> BlockContainerElements = new string[]
    {
        "html", "body", "div", "section", "article", "header", "footer", "main", "aside", "nav",
        "figure", "figcaption", "address", "center", "details", "summary", "dl", "form", "fieldset",
        "table", "thead", "tbody", "tfoot", "tr", "caption"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Elements that pass their children through unchanged.</summary>
    private static readonly FrozenSet<string> InlineContainerElements = new string[]
    {
        "span", "font", "abbr", "acronym", "q", "time", "label", "mark", "bdi", "bdo", "data",
        "picture", "nobr", "ruby", "rt", "rp"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>A size the content asks for, in pixels or as a percentage of what is available.</summary>
    private readonly record struct HtmlLength(double Value, bool IsPercentage);

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
    private readonly bool _areImagesEnabled;

    private bool _hasBlockedImages;
    private Paragraph? _paragraph;
    private Hyperlink? _hyperlink;
    private Uri? _hyperlinkTarget;
    private Run? _lastRun;
    private bool _trimLeadingSpace = true;
    private double _indent;
    private TextFormat _format = TextFormat.Default;

    private HtmlFlowBuilder(double baseFontSize, Action<Uri> linkActivated, bool areImagesEnabled)
    {
        _baseFontSize = baseFontSize;
        _linkActivated = linkActivated;
        _areImagesEnabled = areImagesEnabled;
    }

    /// <param name="linkActivated">Called when a link is clicked; the builder never navigates itself.</param>
    /// <param name="areImagesEnabled">
    /// Whether pictures are fetched and drawn. Fetching one is a request to wherever the content
    /// says, which is how a sender learns that the message was opened, so it is asked for rather
    /// than assumed — see <see cref="HtmlFlowResult.HasBlockedImages"/> for what was held back.
    /// </param>
    public static HtmlFlowResult Build(
        string html,
        double baseFontSize,
        Action<Uri> linkActivated,
        bool areImagesEnabled)
    {
        var builder = new HtmlFlowBuilder(baseFontSize, linkActivated, areImagesEnabled);
        var document = HtmlDocument.Parse(html);

        // Without a body the content is a fragment, and all of it is content; the head and its
        // children are dropped as ignored elements either way.
        if (document.TryGetBody(out var body))
        {
            builder.VisitChildren(body, TextFormat.Default);
        }
        else
        {
            builder.VisitDocument(document);
        }

        builder.EndParagraph();
        return new HtmlFlowResult(builder._blocks, builder._widthAppliers, builder._hasBlockedImages);
    }

    [GeneratedRegex("[ \t\r\n\f]+")]
    private static partial Regex HtmlWhitespaceRegex();

    /// <summary>Visits the children of an element in the given format, restoring the current one afterwards.</summary>
    private void VisitChildren(HtmlElement element, TextFormat format)
    {
        var outer = _format;
        _format = format;

        foreach (var child in element.Nodes)
        {
            VisitNode(child);
        }

        _format = outer;
    }

    // Comments carry no visible content and are left to the base visitor, which ignores them.
    public override void VisitText(HtmlText text) => AppendText(text.Text, _format);

    public override void VisitElement(HtmlElement element)
    {
        var format = _format;

        // The parser keeps names as written, so they are matched in their canonical lowercase form.
        switch (element.Name.ToLowerInvariant())
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

            case "video":
                AppendVideo(element, format);
                break;

            case "a":
                VisitChildren(element, format with { Link = ResolveLink(element.GetAttribute("href")?.Value) ?? format.Link });
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

    private void VisitOtherElement(HtmlElement element, TextFormat format)
    {
        var name = element.Name;

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
        AppendText(element.ToString(), format);
    }

    private void VisitBlock(HtmlElement element, TextFormat format, double topSpacing, double extraIndent = 0)
    {
        EndParagraph();
        _indent += extraIndent;
        StartParagraph(topSpacing);
        VisitChildren(element, format);
        EndParagraph();
        _indent -= extraIndent;
    }

    private void VisitList(HtmlElement element, TextFormat format)
    {
        EndParagraph();

        var start = int.TryParse(element.GetAttribute("start")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStart)
            ? parsedStart
            : 1;

        _lists.Push(new ListState(element.Name.Equals("ol", StringComparison.OrdinalIgnoreCase), start));
        _indent += ListIndent;
        VisitChildren(element, format);
        _indent -= ListIndent;
        _lists.Pop();

        EndParagraph();
    }

    private void VisitListItem(HtmlElement element, TextFormat format)
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

    private void VisitTableCell(HtmlElement element, TextFormat format)
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

    private void AppendImage(HtmlElement element, TextFormat format)
    {
        var source = element.GetAttribute("src")?.Value;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = element.GetAttribute("data-src")?.Value;
        }

        // A picture this cannot fetch — one carried inside the message, or addressed in some scheme
        // of its own — is not one that showing pictures would bring back, so it is what it says it
        // is and nothing is held back.
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            AppendAlternativeText(element, format);
            return;
        }

        if (IsTrackingPixel(element))
        {
            return;
        }

        if (!_areImagesEnabled)
        {
            // Counted rather than drawn, so that what was held back can be offered to the reader
            // instead of quietly left out.
            _hasBlockedImages = true;
            AppendAlternativeText(element, format);
            return;
        }

        var image = new Image
        {
            Source = new BitmapImage(uri),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left
        };

        var requestedWidth = ReadLength(element, "width");
        var requestedHeight = ReadLength(element, "height");

        // Images sit directly in the paragraph: an InlineUIContainer is not allowed inside a Hyperlink.
        EnsureParagraph().Inlines.Add(new InlineUIContainer { Child = image });
        _widthAppliers.Add(width => ApplyImageSize(image, requestedWidth, requestedHeight, width));
        _hyperlink = null;
        _hyperlinkTarget = null;
        _lastRun = null;
        _trimLeadingSpace = false;
    }

    /// <summary>
    /// A video the content addresses over the web: its own src, else the first source child that has
    /// one. A blob address only means something inside the page that made it, and every other scheme
    /// is nothing this can fetch, so such a video is left as the markup it came as.
    /// </summary>
    private void AppendVideo(HtmlElement element, TextFormat format)
    {
        if (FindVideoSource(element) is not { } uri)
        {
            VisitOtherElement(element, format);
            return;
        }

        if (!_areImagesEnabled)
        {
            // a video is fetched from wherever the content says just like a picture is
            _hasBlockedImages = true;
            return;
        }

        var player = new MediaPlayerElement
        {
            Source = MediaSource.CreateFromUri(uri),
            AreTransportControlsEnabled = true,
            AutoPlay = false,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
        };

        if (ResolveMediaUri(element.GetAttribute("poster")?.Value) is { } poster)
        {
            player.PosterSource = new BitmapImage(poster);
        }

        var requestedWidth = ReadLength(element, "width");
        var requestedHeight = ReadLength(element, "height");

        // like images, the player sits directly in the paragraph, never inside a hyperlink
        EnsureParagraph().Inlines.Add(new InlineUIContainer { Child = player });
        _widthAppliers.Add(width => ApplyVideoSize(player, requestedWidth, requestedHeight, width));
        _hyperlink = null;
        _hyperlinkTarget = null;
        _lastRun = null;
        _trimLeadingSpace = false;
    }

    private static Uri? FindVideoSource(HtmlElement element)
    {
        if (ResolveMediaUri(element.GetAttribute("src")?.Value) is { } source)
        {
            return source;
        }

        foreach (var child in element.Nodes)
        {
            if (child is HtmlElement { Name: var name } sourceElement &&
                name.Equals("source", StringComparison.OrdinalIgnoreCase) &&
                ResolveMediaUri(sourceElement.GetAttribute("src")?.Value) is { } childSource)
            {
                return childSource;
            }
        }

        return null;
    }

    /// <summary>Only web addresses are fetched: blob, data, file and every other scheme are not.</summary>
    private static Uri? ResolveMediaUri(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is ("http" or "https")
            ? uri
            : null;

    /// <summary>
    /// Sizes a video like a picture, except that a player shows nothing until the video has loaded,
    /// so one that was not given a height is held at the usual 16:9 rather than collapsing.
    /// </summary>
    private static void ApplyVideoSize(MediaPlayerElement player, HtmlLength? width, HtmlLength? height, double available)
    {
        ApplyImageSize(player, width, height, available);

        if (double.IsNaN(player.Height))
        {
            var actualWidth = double.IsNaN(player.Width) ? available : player.Width;
            player.Height = actualWidth * 9 / 16;
        }
    }

    /// <summary>
    /// What a picture that is not drawn leaves in its place: what the sender wrote it as, where they
    /// wrote one. A picture nobody described leaves nothing rather than a gap of its own.
    /// </summary>
    private void AppendAlternativeText(HtmlElement element, TextFormat format)
    {
        if (element.GetAttribute("alt")?.Value is { } alt && !string.IsNullOrWhiteSpace(alt))
        {
            AppendText(alt, format);
        }
    }

    private static bool IsTrackingPixel(HtmlElement element) =>
        ReadLength(element, "width") is { IsPercentage: false, Value: <= 1 } &&
        ReadLength(element, "height") is { IsPercentage: false, Value: <= 1 };

    /// <summary>
    /// Sizes a picture the way the content asks, within the room the control actually has: a
    /// percentage is of that room, and a picture asking for more than there is is scaled down whole
    /// rather than squashed or cropped.
    /// </summary>
    private static void ApplyImageSize(FrameworkElement image, HtmlLength? width, HtmlLength? height, double available)
    {
        image.MaxWidth = available;

        double? finalWidth = width is { } w
            ? w.IsPercentage ? available * w.Value / 100 : w.Value
            : null;

        // A percentage height is of the height of what contains the picture, which a flow of text
        // does not have one of, so only pixel heights are honoured.
        double? finalHeight = height is { IsPercentage: false } h ? h.Value : null;

        if (finalWidth > available)
        {
            // Keep the proportions the content asked for while fitting what there is room for.
            finalHeight = finalHeight * available / finalWidth;
            finalWidth = available;
        }

        if (finalWidth is { } widthValue)
        {
            image.Width = widthValue;
        }

        if (finalHeight is { } heightValue)
        {
            image.Height = heightValue;
        }
    }

    /// <summary>
    /// The size the content asks an element for: the inline style if it says one, else the attribute
    /// of the same name, a style declaration winning over the attribute it duplicates.
    /// </summary>
    private static HtmlLength? ReadLength(HtmlElement element, string property) =>
        ParseLength(ReadStyleProperty(element.GetAttribute("style")?.Value, property)) ??
        ParseLength(element.GetAttribute(property)?.Value);

    /// <summary>
    /// Reads one declaration out of an inline style. Deliberately crude — the style is split into
    /// declarations and each into a name and a value, and anything that does not come apart that way
    /// is passed over: what a real stylesheet parser would buy here is not worth what it costs.
    /// </summary>
    private static string? ReadStyleProperty(string? style, string property)
    {
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        string? value = null;

        foreach (var declaration in style.Split(';'))
        {
            var separator = declaration.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0 ||
                !declaration.AsSpan(0, separator).Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Kept looking rather than returned: the last declaration of a property is the one that
            // counts, as it does in CSS.
            value = declaration[(separator + 1)..].Trim();
        }

        return value;
    }

    /// <summary>
    /// A number followed by an optional unit. Only pixels and percentages are read: everything else
    /// — em, cm, calc(), auto, inherit — has no length this can work out on its own, so it is left
    /// for the layout to decide as if nothing had been asked for.
    /// </summary>
    private static HtmlLength? ParseLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.AsSpan().Trim();
        var isPercentage = value.EndsWith("%", StringComparison.Ordinal);

        if (isPercentage)
        {
            value = value[..^1];
        }
        else if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }

        return double.TryParse(value.TrimEnd(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number) &&
            number >= 0
                ? new HtmlLength(number, isPercentage)
                : null;
    }

    /// <summary>
    /// Only safe https addresses become links: feed content is untrusted, so file, javascript, data
    /// and every other scheme, as well as non-ASCII and loopback addresses, are left as plain text
    /// rather than handed to the shell. See <see cref="LinkSafety"/>.
    /// </summary>
    private static Uri? ResolveLink(string? href) => LinkSafety.TryCreate(href?.Trim());

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
/// <param name="HasBlockedImages">
/// Whether the content carries a picture that was not drawn because pictures are turned off — the
/// ones that showing them would actually bring back, so this is what there is to offer the reader.
/// Trackers are left out of it: a pixel nobody can see is not a picture being held back.
/// </param>
internal sealed record HtmlFlowResult(
    IReadOnlyList<Block> Blocks,
    IReadOnlyList<Action<double>> WidthAppliers,
    bool HasBlockedImages);
