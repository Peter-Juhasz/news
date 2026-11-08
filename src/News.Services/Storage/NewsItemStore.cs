using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using News.Configuration;

namespace News.Storage;

/// <summary>
/// Stores each group's items in a JSON file of its own, so a group written while another one is
/// still loading leaves that one's file alone.
/// </summary>
public sealed partial class NewsItemStore : INewsItemStore
{
    private const string FileExtension = ".json";

    /// <summary>
    /// Length the readable part of a file name is cut to; the name stays a name rather than a
    /// group title spelled out in full.
    /// </summary>
    private const int MaximumSlugLength = 32;

    private readonly ILogger<NewsItemStore> _logger;
    private readonly string _directoryPath;

    public NewsItemStore(IOptions<NewsReaderOptions> options, ILogger<NewsItemStore> logger)
    {
        _logger = logger;
        _directoryPath = ConfiguredPath.Resolve(options.Value.CacheDirectoryPath);
    }

    public async Task<NewsGroupSnapshot?> LoadAsync(string groupTitle, CancellationToken cancellationToken)
    {
        var filePath = ResolveFilePath(groupTitle);

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync(stream, NewsItemStoreJsonContext.Default.NewsGroupSnapshot, cancellationToken);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Failed to read the stored items of group '{Group}'.", groupTitle);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Not allowed to read the stored items of group '{Group}'.", groupTitle);
        }
        catch (JsonException exception)
        {
            // A file written by an older shape of these records, or one left half-written by a
            // machine that lost power mid-save. The next full load replaces it.
            _logger.LogWarning(exception, "Discarding the unreadable stored items of group '{Group}'.", groupTitle);
        }

        return null;
    }

    public async Task SaveAsync(string groupTitle, NewsGroupSnapshot snapshot, CancellationToken cancellationToken)
    {
        var filePath = ResolveFilePath(groupTitle);

        // Written beside the file and moved onto it, so a save that does not finish leaves the
        // previous items readable rather than a truncated file in their place.
        var temporaryPath = filePath + ".tmp";

        try
        {
            Directory.CreateDirectory(_directoryPath);

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, NewsItemStoreJsonContext.Default.NewsGroupSnapshot, cancellationToken);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Failed to store the items of group '{Group}'.", groupTitle);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Not allowed to store the items of group '{Group}'.", groupTitle);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
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

    private string ResolveFilePath(string groupTitle) =>
        Path.Combine(_directoryPath, ResolveFileName(groupTitle));

    /// <summary>
    /// Names a group's file after the group, and after a hash of its title so that two titles
    /// that read the same once punctuation is dropped still get a file each.
    /// </summary>
    private static string ResolveFileName(string groupTitle)
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

        // A title written in a script this keeps nothing of is left to the hash alone.
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
