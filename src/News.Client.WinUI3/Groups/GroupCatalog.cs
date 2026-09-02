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
    private readonly object _timerLock = new();
    private readonly Timer _reloadTimer;
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
        _reloadTimer = new Timer(static state => ((GroupCatalog)state!).ScheduleReloadAsync(), this, Timeout.Infinite, Timeout.Infinite);
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
        _reloadTimer.Dispose();
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
        lock (_timerLock)
        {
            _reloadTimer.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        }
    }

    private async void ScheduleReloadAsync()
    {
        try
        {
            await ReloadAsync(CancellationToken.None);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Reload failed due to file access issue.");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Reload failed because the groups folder is inaccessible.");
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(exception, "Reload failed because a group file is invalid.");
        }
        catch (YamlException exception)
        {
            _logger.LogWarning(exception, "Reload failed because a group file is not valid YAML.");
        }
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

    private static string ResolveGroupsPath(string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
}
