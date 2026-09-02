using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using News.Client.WinUI3.Configuration;
using News.Domain;
using YamlDotNet.Core;

namespace News.Client.WinUI3.Groups;

public sealed class GroupCatalog : IGroupCatalog, IDisposable
{
    private readonly GroupFileLoader _groupFileLoader;
    private readonly ILogger<GroupCatalog> _logger;
    private readonly string _groupsPath;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private SynchronizationContext? _uiContext;

    public GroupCatalog(
        GroupFileLoader groupFileLoader,
        IOptions<NewsReaderOptions> options,
        ILogger<GroupCatalog> logger)
    {
        _groupFileLoader = groupFileLoader;
        _logger = logger;
        _groupsPath = ResolveGroupsPath(options.Value.GroupsPath);
    }

    public event EventHandler<IReadOnlyList<NewsGroup>>? GroupsChanged;

    public IReadOnlyList<NewsGroup> CurrentGroups { get; private set; } = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _uiContext ??= SynchronizationContext.Current;

        Directory.CreateDirectory(_groupsPath);
        await ReloadAsync(cancellationToken);
        EnsureWatcher();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _reloadLock.Dispose();
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = new FileSystemWatcher(_groupsPath, "*.group.yml")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnGroupFilesChanged;
        _watcher.Changed += OnGroupFilesChanged;
        _watcher.Deleted += OnGroupFilesChanged;
        _watcher.Renamed += OnGroupFilesChanged;
    }

    private void OnGroupFilesChanged(object sender, FileSystemEventArgs eventArgs)
    {
        // TODO: reload
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            var groups = new List<NewsGroup>();
            foreach (var filePath in Directory.EnumerateFiles(_groupsPath, "*.group.yml", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    groups.Add(_groupFileLoader.LoadFromPath(filePath));
                }
                catch (IOException exception)
                {
                    _logger.LogWarning(exception, "Skipping group file '{GroupFile}' due to file access error.", filePath);
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.LogWarning(exception, "Skipping group file '{GroupFile}' due to permission error.", filePath);
                }
                catch (InvalidDataException exception)
                {
                    _logger.LogWarning(exception, "Skipping group file '{GroupFile}' because it is invalid.", filePath);
                }
                catch (YamlException exception)
                {
                    _logger.LogWarning(exception, "Skipping group file '{GroupFile}' because YAML parsing failed.", filePath);
                }
            }

            CurrentGroups = groups.OrderBy(group => group.Title, StringComparer.OrdinalIgnoreCase).ToList();
            PublishGroupsChanged();
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private void PublishGroupsChanged()
    {
        var handler = GroupsChanged;
        if (handler is null)
        {
            return;
        }

        if (_uiContext is null)
        {
            handler(this, CurrentGroups);
            return;
        }

        _uiContext.Post(_ => handler(this, CurrentGroups), null);
    }

	private static string ResolveGroupsPath(string configuredPath)
	{
        configuredPath = Environment.ExpandEnvironmentVariables(configuredPath);

        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
		}

		return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
	}
}
