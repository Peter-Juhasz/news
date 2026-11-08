using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace News.Client.WinUI3.Controls;

/// <summary>
/// Shows an HTML fragment as native flow text: the markup is parsed and its block and inline
/// structure is rebuilt inside a <see cref="RichTextBlock"/>, so feed content renders with the
/// app's fonts and theme instead of a web view.
/// </summary>
public sealed partial class HtmlTextBlock : UserControl
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
    private bool _isConfirmingLink;

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

        var result = HtmlFlowBuilder.Build(html, FontSize, OnLinkActivated, areImagesEnabled: true);

        foreach (var block in result.Blocks)
        {
            _richTextBlock.Blocks.Add(block);
        }

        _widthAppliers = result.WidthAppliers;
        ApplyWidth(ActualWidth);
    }

    /// <summary>
    /// Feed content decides where its links point, so the address is shown for approval before the
    /// browser is opened.
    /// </summary>
    private async void OnLinkActivated(Uri uri)
    {
        if (_isConfirmingLink || XamlRoot is null)
        {
            return;
        }

        _isConfirmingLink = true;

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Open this link?",
                Content = new TextBlock
                {
                    Text = uri.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                },
                PrimaryButtonText = "Open",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(uri);
            }
        }
        finally
        {
            _isConfirmingLink = false;
        }
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
