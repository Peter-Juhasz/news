using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PeterJuhasz.Repositories.Abstractions;
using PeterJuhasz.Repositories.Blobs;
using PeterJuhasz.Repositories.Locking;

namespace News.Storage;

/// <summary>
/// Stores every bookmark in one blob of the partition it is given.
/// </summary>
public sealed partial class BookmarkStore : IBookmarkStore
{
    private readonly IBlobPartition _partition;
    private readonly string _name;
    private readonly IObjectRepository<BookmarkCollection> _repository;
    private readonly ILogger<BookmarkStore> _logger;

    public BookmarkStore(IBlobPartition partition, string name, ILogger<BookmarkStore> logger)
    {
        _partition = partition;
        _name = name;
        _logger = logger;
        _repository = partition.GetBlob(name)
            .WithLocking()
            .AsJsonObjectRepository(BookmarkStoreJsonContext.Default.BookmarkCollection);
    }

    public async Task<BookmarkCollection?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _repository.GetOrDefaultAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            LogReadFailed(exception, _name);
        }
        catch (UnauthorizedAccessException exception)
        {
            LogReadNotAllowed(exception, _name);
        }
        catch (JsonException exception)
        {
            // Unlike the items a group caches, bookmarks are not something a later load brings
            // back: whatever cannot be read here is the only copy of it. So it is set aside under
            // a name of its own rather than left to be written over, and can still be repaired
            // by hand.
            LogSettingAside(exception, _name);
            await TrySetAsideUnreadableAsync(cancellationToken);
        }

        return null;
    }

    public async Task SaveAsync(BookmarkCollection bookmarks, CancellationToken cancellationToken)
    {
        try
        {
            // written unconditionally rather than read, changed and written back: whatever is
            // stored is replaced whole, even when it could not be read.
            await _repository.StoreAsync(bookmarks, IBlob.AnyOrNoneConcurrencyToken, cancellationToken);
        }
        catch (IOException exception)
        {
            LogStoreFailed(exception, _name);
        }
        catch (UnauthorizedAccessException exception)
        {
            LogStoreNotAllowed(exception, _name);
        }
    }

    /// <summary>
    /// Copies unreadable bookmarks out of the way and removes them, under a name carrying the
    /// moment they were set aside so an earlier copy is not written over.
    /// </summary>
    private async Task TrySetAsideUnreadableAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var setAsideName = $"{_name}.corrupt-{timestamp}";
        var blob = _partition.GetBlob(_name);

        try
        {
            if (await blob.ReadAsync(cancellationToken) is not { } unreadable)
            {
                return;
            }

            await _partition.GetBlob(setAsideName).WriteAsync(
                unreadable.Value.ToMemory(),
                IBlob.AnyOrNoneConcurrencyToken,
                new(MediaType: unreadable.Info.MediaType),
                cancellationToken);
            await blob.DeleteAsync(unreadable.Info.ConcurrencyToken, cancellationToken);

            LogSetAside(setAsideName);
        }
        catch (IOException exception)
        {
            LogSetAsideFailed(exception, _name);
        }
        catch (UnauthorizedAccessException exception)
        {
            LogSetAsideNotAllowed(exception, _name);
        }
        catch (ConflictException exception)
        {
            // changed since it was read, so what was copied aside is no longer what is there
            LogSetAsideConflict(exception, _name);
        }
    }

    /// <summary>
    /// With the fields a story leaves unset left out rather than spelled out as nulls. Generated
    /// at build time rather than reflected over at run time, which a natively compiled app has
    /// nothing to reflect with.
    /// </summary>
    [JsonSourceGenerationOptions(
        JsonSerializerDefaults.Web,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(BookmarkCollection))]
    private sealed partial class BookmarkStoreJsonContext : JsonSerializerContext;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the bookmarks '{Name}'.")]
    private partial void LogReadFailed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Not allowed to read the bookmarks '{Name}'.")]
    private partial void LogReadNotAllowed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Setting aside the unreadable bookmarks '{Name}'.")]
    private partial void LogSettingAside(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to store the bookmarks in '{Name}'.")]
    private partial void LogStoreFailed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Not allowed to store the bookmarks in '{Name}'.")]
    private partial void LogStoreNotAllowed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The unreadable bookmarks were set aside as '{Name}'.")]
    private partial void LogSetAside(string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to set aside the unreadable bookmarks '{Name}'.")]
    private partial void LogSetAsideFailed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Not allowed to set aside the unreadable bookmarks '{Name}'.")]
    private partial void LogSetAsideNotAllowed(Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The unreadable bookmarks '{Name}' changed while being set aside.")]
    private partial void LogSetAsideConflict(Exception exception, string name);
}
