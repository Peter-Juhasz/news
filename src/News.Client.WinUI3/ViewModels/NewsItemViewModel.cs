using News.Client.WinUI3.Services;
using News.Domain;

namespace News.Client.WinUI3.ViewModels;

public sealed class NewsItemViewModel(NewsItem item, NewsSource source)
{
    public string? Id { get; } = item.Id;

    public DateTimeOffset Date { get; } = item.Date;

    public string Title { get; } = item.Title;

    public string Lead { get; } = item.Lead;

    public string Url { get; } = item.Url;

    public Uri? UrlUri { get; } = Uri.TryCreate(item.Url, UriKind.Absolute, out var urlUri) ? urlUri : null;

    public string? ImageUrl { get; } = item.ImageUrl;

    public Uri? ImageUri { get; } = Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var imageUri) ? imageUri : null;

    public string? Content { get; } = item.Content;

    /// <summary>
    /// Name configured on the source, shown instead of the article's host name.
    /// </summary>
    public string? SourceTitle { get; } = source.Title;

    /// <summary>
    /// View this item's source prefers when the item is selected.
    /// </summary>
    public NewsContentMode? DefaultView { get; } = source.DefaultView;

    public bool HasImage => ImageUri is not null;

    public string TimeText => Date.ToLocalTime().ToString("HH:mm");

    public string PlainContent => HtmlUtility.ToPlainText(Content);

    public string? HtmlContent => HtmlUtility.LooksLikeHtml(Content) ? Content : null;
}
