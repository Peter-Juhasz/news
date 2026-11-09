using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PeterJuhasz.Repositories.Abstractions;
using PeterJuhasz.Repositories.Blobs;
using PeterJuhasz.Repositories.Locking;

namespace News.Storage;

/// <summary>
/// Stores each group's items in a blob of its own in the partition it is given, so a group written
/// while another one is still loading leaves that one's blob alone.
/// </summary>
public sealed partial class NewsItemStore(IBlobPartition partition, ILogger<NewsItemStore> logger) : INewsItemStore
{
    private const string FileExtension = ".json";

    /// <summary>
    /// Length the readable part of a blob name is cut to; the name stays a name rather than a
    /// group title spelled out in full.
    /// </summary>
    private const int MaximumSlugLength = 32;

    /// <summary>
    /// One repository per blob, kept for the life of the store: the lock that keeps a save and a
    /// load of the same group apart lives in the repository, so a new one each time would lock
    /// nothing.
    /// </summary>
    private readonly ConcurrentDictionary<string, IObjectRepository<NewsGroupSnapshot>> _repositories = new();

    public async Task<NewsGroupSnapshot?> LoadAsync(string groupTitle, CancellationToken cancellationToken)
    {
        try
        {
            return await GetRepository(groupTitle).GetOrDefaultAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to read the stored items of group '{Group}'.", groupTitle);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Not allowed to read the stored items of group '{Group}'.", groupTitle);
        }
        catch (JsonException exception)
        {
            // written by an older shape of these records, or left half-written by a machine that
            // lost power mid-save. the next full load replaces it.
            logger.LogWarning(exception, "Discarding the unreadable stored items of group '{Group}'.", groupTitle);
        }

        return null;
    }

    public async Task SaveAsync(string groupTitle, NewsGroupSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            // written unconditionally: whatever is stored is replaced whole, even when it could
            // not be read.
            await GetRepository(groupTitle).StoreAsync(snapshot, IBlob.AnyOrNoneConcurrencyToken, cancellationToken);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to store the items of group '{Group}'.", groupTitle);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Not allowed to store the items of group '{Group}'.", groupTitle);
        }
    }

    private IObjectRepository<NewsGroupSnapshot> GetRepository(string groupTitle) =>
        _repositories.GetOrAdd(
            ResolveBlobName(groupTitle),
            static (name, partition) => partition.GetBlob(name)
                .WithLocking()
                .AsJsonObjectRepository(NewsItemStoreJsonContext.Default.NewsGroupSnapshot),
            partition);

    /// <summary>
    /// Names a group's blob after the group, and after a hash of its title so that two titles
    /// that read the same once punctuation is dropped still get a blob each.
    /// </summary>
    private static string ResolveBlobName(string groupTitle)
    {
        var title = groupTitle.ToLowerInvariant();
        return $"{ToSlug(title)}-{ToHash(title)}{FileExtension}";
    }

    private static string ToSlug(string title)
    {
        var slug = new StringBuilder(MaximumSlugLength);

        foreach (var character in title)
        {
            if (slug.Length == MaximumSlugLength)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        // a title written in a script this keeps nothing of is left to the hash alone.
        return slug.ToString().TrimEnd('-') is { Length: > 0 } trimmed ? trimmed : "group";
    }

    private static string ToHash(string title)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(title));
        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    /// <summary>
    /// Serialization code generated at build time rather than reflected over at run time, which a
    /// natively compiled app has nothing to reflect with.
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(NewsGroupSnapshot))]
    private sealed partial class NewsItemStoreJsonContext : JsonSerializerContext;
}
