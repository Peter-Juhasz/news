using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using News.Configuration;

namespace News.Storage;

/// <summary>
/// Stores every bookmark in the one file configuration names.
/// </summary>
public sealed partial class BookmarkStore : IBookmarkStore
{
    private readonly ILogger<BookmarkStore> _logger;
    private readonly string _filePath;

    public BookmarkStore(IOptions<NewsReaderOptions> options, ILogger<BookmarkStore> logger)
    {
        _logger = logger;
        _filePath = ConfiguredPath.Resolve(options.Value.BookmarksFilePath);
    }

    public async Task<BookmarkCollection?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync(stream, BookmarkStoreJsonContext.Default.BookmarkCollection, cancellationToken);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Failed to read the bookmarks file '{File}'.", _filePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Not allowed to read the bookmarks file '{File}'.", _filePath);
        }
        catch (JsonException exception)
        {
            // Unlike the items a group caches, bookmarks are not something a later load brings
            // back: whatever cannot be read here is the only copy of it. So the file is set aside
            // under a name of its own rather than left to be written over, and can still be
            // repaired by hand.
            _logger.LogWarning(exception, "Setting aside the unreadable bookmarks file '{File}'.", _filePath);
            TrySetAsideUnreadableFile();
        }

        return null;
    }

    public async Task SaveAsync(BookmarkCollection bookmarks, CancellationToken cancellationToken)
    {
        // Written beside the file and moved onto it, so a save that does not finish leaves the
        // previous bookmarks readable rather than a truncated file in their place.
        var temporaryPath = _filePath + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, bookmarks, BookmarkStoreJsonContext.Default.BookmarkCollection, cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Failed to store the bookmarks in '{File}'.", _filePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Not allowed to store the bookmarks in '{File}'.", _filePath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    /// <summary>
    /// Moves an unreadable file out of the way, under a name carrying the moment it was set
    /// aside so an earlier one is not written over.
    /// </summary>
    private void TrySetAsideUnreadableFile()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var setAsidePath = $"{_filePath}.corrupt-{timestamp}";

        try
        {
            File.Move(_filePath, setAsidePath, overwrite: true);
            _logger.LogWarning("The unreadable bookmarks file was set aside as '{File}'.", setAsidePath);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Failed to set aside the unreadable bookmarks file '{File}'.", _filePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Not allowed to set aside the unreadable bookmarks file '{File}'.", _filePath);
        }
    }

    private void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Failed to delete the temporary file '{File}'.", temporaryPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Not allowed to delete the temporary file '{File}'.", temporaryPath);
        }
    }

    /// <summary>
    /// Indented, and with the fields a story leaves unset left out rather than spelled out as
    /// nulls: this file is the reader's own, and is meant to be legible if they open it. Generated
    /// at build time rather than reflected over at run time, which a natively compiled app has
    /// nothing to reflect with.
    /// </summary>
    [JsonSourceGenerationOptions(
        JsonSerializerDefaults.Web,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(BookmarkCollection))]
    private sealed partial class BookmarkStoreJsonContext : JsonSerializerContext;
}
