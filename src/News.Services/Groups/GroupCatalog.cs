using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using News.Configuration;
using News.Domain;
using YamlDotNet.Core;

namespace News.Groups;

public sealed class GroupCatalog : IGroupCatalog, IDisposable
{
    private readonly GroupsFileLoader _groupsFileLoader;
    private readonly ILogger<GroupCatalog> _logger;
    private readonly string _groupsFilePath;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private SynchronizationContext? _uiContext;

    public GroupCatalog(
        GroupsFileLoader groupsFileLoader,
        IOptions<NewsReaderOptions> options,
        ILogger<GroupCatalog> logger)
    {
        _groupsFileLoader = groupsFileLoader;
        _logger = logger;
        _groupsFilePath = ResolveGroupsFilePath(options.Value.GroupsFilePath);
    }

    public event EventHandler<IReadOnlyList<NewsGroup>>? GroupsChanged;

    public IReadOnlyList<NewsGroup> CurrentGroups { get; private set; } = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _uiContext ??= SynchronizationContext.Current;

        Directory.CreateDirectory(Path.GetDirectoryName(_groupsFilePath)!);
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

        _watcher = new FileSystemWatcher(
            Path.GetDirectoryName(_groupsFilePath)!,
            Path.GetFileName(_groupsFilePath))
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnGroupsFileChanged;
        _watcher.Changed += OnGroupsFileChanged;
        _watcher.Deleted += OnGroupsFileChanged;
        _watcher.Renamed += OnGroupsFileChanged;
    }

    private void OnGroupsFileChanged(object sender, FileSystemEventArgs eventArgs)
    {
        // TODO: reload
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            var groups = new List<NewsGroup>();

            if (!File.Exists(_groupsFilePath))
            {
                _logger.LogWarning("Groups file '{GroupsFile}' was not found.", _groupsFilePath);
            }
            else
            {
                try
                {
                    groups.AddRange(_groupsFileLoader.LoadFromPath(_groupsFilePath));
                }
                catch (IOException exception)
                {
                    _logger.LogWarning(exception, "Skipping groups file '{GroupsFile}' due to file access error.", _groupsFilePath);
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.LogWarning(exception, "Skipping groups file '{GroupsFile}' due to permission error.", _groupsFilePath);
                }
                catch (InvalidDataException exception)
                {
                    _logger.LogWarning(exception, "Skipping groups file '{GroupsFile}' because it is invalid.", _groupsFilePath);
                }
                catch (YamlException exception)
                {
                    _logger.LogWarning(exception, "Skipping groups file '{GroupsFile}' because YAML parsing failed.", _groupsFilePath);
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

	private static string ResolveGroupsFilePath(string configuredPath)
	{
        configuredPath = Environment.ExpandEnvironmentVariables(configuredPath);

        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
		}

		return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
	}
}
