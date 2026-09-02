using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace News.Client.WinUI3.Controls;

/// <summary>
/// Shows an HTML fragment as native flow text: the markup is parsed and its block and inline
/// structure is rebuilt inside a <see cref="RichTextBlock"/>, so feed content renders with the
/// app's fonts and theme instead of a web view.
/// </summary>
public sealed class HtmlTextBlock : UserControl
{
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.Register(
        nameof(Html),
        typeof(string),
        typeof(HtmlTextBlock),
        new PropertyMetadata(null, static (sender, _) => ((HtmlTextBlock)sender).Rebuild()));

    private readonly RichTextBlock _richTextBlock = new()
    {
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true
    };

    private IReadOnlyList<Action<double>> _widthAppliers = [];

    public HtmlTextBlock()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        Content = _richTextBlock;
        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// HTML fragment to display. Null or blank clears the control.
    /// </summary>
    public string? Html
    {
        get => (string?)GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    public bool IsTextSelectionEnabled
    {
        get => _richTextBlock.IsTextSelectionEnabled;
        set => _richTextBlock.IsTextSelectionEnabled = value;
    }

    private void Rebuild()
    {
        _richTextBlock.Blocks.Clear();
        _widthAppliers = [];

        var html = Html;
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        var result = HtmlFlowBuilder.Build(html, FontSize);

        foreach (var block in result.Blocks)
        {
            _richTextBlock.Blocks.Add(block);
        }

        _widthAppliers = result.WidthAppliers;
        ApplyWidth(ActualWidth);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyWidth(e.NewSize.Width);

    private void ApplyWidth(double width)
    {
        if (width <= 0 || double.IsNaN(width))
        {
            return;
        }

        foreach (var apply in _widthAppliers)
        {
            apply(width);
        }
    }
}
