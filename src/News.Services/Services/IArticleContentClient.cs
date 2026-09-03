namespace News.Services;

public interface IArticleContentClient
{
    /// <summary>
    /// Loads the page at <paramref name="url"/> and returns the HTML of the first element
    /// <paramref name="selector"/> matches, or null where it matches nothing.
    /// </summary>
    Task<string?> LoadAsync(string url, string selector, CancellationToken cancellationToken);
}
