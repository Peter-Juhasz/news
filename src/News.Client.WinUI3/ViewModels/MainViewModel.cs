using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using News.Domain;
using News.Groups;
using News.Services;
using News.Storage;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace News.Client.WinUI3.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// How long typing has to pause before the search text reaches the list. Long enough that a
    /// word typed at speed regroups the list once rather than once per letter, short enough that
    /// the pause is not read as the list having stopped responding.
    /// </summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly IGroupCatalog _groupCatalog;
    private readonly INewsSourceClient _newsSourceClient;
    private readonly IArticleContentClient _articleContentClient;
    private readonly INewsItemStore _newsItemStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainViewModel> _logger;
    private readonly SemaphoreSlim _selectionLock = new(1, 1);

    /// <summary>
    /// Every row of the selected group, before the search text narrows it down. Held so typing
    /// re-groups this list instead of merging the group's sources again on each keystroke.
    /// </summary>
    private readonly List<NewsItemViewModel> _items = [];

    /// <summary>
    /// Every keystroke in the search box, on its way to <see cref="ApplyQuery"/>.
    /// </summary>
    private readonly Subject<string> _searchTextInput = new();

    /// <summary>
    /// The trimmed query <see cref="DayGroups"/> was last built against. It trails
    /// <see cref="SearchText"/> by the throttle window while a word is being typed.
    /// </summary>
    private string _appliedQuery = string.Empty;

    private bool _initialized;

    /// <summary>
    /// Cancels the article body being fetched for the selected item; also the token the running
    /// fetch is matched against, so a fetch overtaken by a newer selection leaves the pane alone.
    /// </summary>
    private CancellationTokenSource? _contentLoad;

    public MainViewModel(
        IGroupCatalog groupCatalog,
        INewsSourceClient newsSourceClient,
        IArticleContentClient articleContentClient,
        INewsItemStore newsItemStore,
        ILoggerFactory loggerFactory)
    {
        _groupCatalog = groupCatalog;
        _newsSourceClient = newsSourceClient;
        _articleContentClient = articleContentClient;
        _newsItemStore = newsItemStore;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MainViewModel>();

        RefreshSelectedGroupCommand = new AsyncRelayCommand(
            execute: RefreshSelectedGroupAsync,
            canExecute: () => SelectedGroup is not null && !IsBusy);

        OpenSelectedArticleCommand = new AsyncRelayCommand(
            execute: OpenSelectedArticleAsync,
            canExecute: () => CanOpenSelected);

        CopySelectedAsTextCommand = new RelayCommand(
            execute: CopySelectedAsText,
            canExecute: () => HasSelection);

        CopySelectedUrlCommand = new RelayCommand(
            execute: CopySelectedUrl,
            canExecute: () => CanOpenSelected);

        CloseSearchCommand = new RelayCommand(execute: CloseSearch);

        // Filtering walks every loaded item, so the text is only applied once typing settles,
        // and only when it settles on something other than the query already showing —
        // backspacing over a letter and typing it again leaves the list alone.
        var searchQueries = _searchTextInput
            .Select(text => text.Trim())
            .Throttle(SearchDelay)
            .DistinctUntilChanged(StringComparer.Ordinal);

        // Throttle's timer runs on a pool thread, while the collections a query rebuilds belong
        // to the thread this view model is built on — the UI one. Nothing unsubscribes: the
        // subscription reaches no further than the subject it reads, both of which live exactly
        // as long as this view model does.
        _ = (SynchronizationContext.Current is { } uiContext
                ? searchQueries.ObserveOn(uiContext)
                : searchQueries)
            .Subscribe(ApplyQuery);
    }

    public ObservableCollection<NewsGroupViewModel> Groups { get; } = [];

    public ObservableCollection<NewsDayGroupViewModel> DayGroups { get; } = [];

    public IAsyncRelayCommand RefreshSelectedGroupCommand { get; }

    public IAsyncRelayCommand OpenSelectedArticleCommand { get; }

    public IRelayCommand CopySelectedAsTextCommand { get; }

    public IRelayCommand CopySelectedUrlCommand { get; }

    public IRelayCommand CloseSearchCommand { get; }

    [ObservableProperty]
    public partial NewsGroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial NewsItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial NewsContentMode SelectedContentMode { get; set; } = NewsContentMode.Text;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Whether the running refresh was started by pulling the list down. The pull's own
    /// visualizer reports that wait, so the spinner over the list stays out of its way.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPullRefreshing { get; set; }

    /// <summary>
    /// Whether the list itself should show that it is loading.
    /// </summary>
    public bool IsListLoading => IsBusy && !IsPullRefreshing;

    /// <summary>
    /// Whether the selected item's body is still being fetched from its own page.
    /// </summary>
    [ObservableProperty]
    public partial bool IsContentLoading { get; set; }

    /// <summary>
    /// Whether the search box has taken the place of the icon button over the list.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public bool IsSearchCollapsed => !IsSearchOpen;

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// A search that hides every row: the list is empty for a reason worth spelling out. Reads
    /// the applied query rather than <see cref="SearchText"/>, so the message appears with the
    /// empty list it explains instead of a throttle window ahead of it.
    /// </summary>
    public bool HasNoMatches => _appliedQuery.Length > 0 && DayGroups.Count == 0;

    public bool HasSelection => SelectedItem is not null;

    public bool HasNoSelection => SelectedItem is null;

    public string SelectedTitle => SelectedItem?.Title ?? "Select a story";

    public string SelectedDateTimeText => SelectedItem?.Date.ToLocalTime().ToString("f") ?? string.Empty;

    public string SelectedSource => SelectedItem?.SourceName ?? string.Empty;

    public string SelectedViewsText => SelectedItem?.ViewsText ?? string.Empty;

    public string SelectedReactionsText => SelectedItem?.ReactionsText ?? string.Empty;

    public string SelectedByline => NewsItemViewModel.Join(
        SelectedSource,
        SelectedDateTimeText,
        SelectedViewsText,
        SelectedReactionsText);

    public string SelectedUrl => SelectedItem?.Url ?? string.Empty;

    public Uri? SelectedUrlUri => SelectedItem?.UrlUri;

    public string SelectedPlainContent => SelectedItem?.PlainContent ?? string.Empty;

    public string? SelectedHtmlContent => SelectedItem?.HtmlContent;

    public bool HasPlainContent => !string.IsNullOrWhiteSpace(SelectedPlainContent);

    public bool HasHtmlContent => !string.IsNullOrWhiteSpace(SelectedHtmlContent);

    public bool HasWebContent => SelectedUrlUri is not null;

    // The content views wait for the body being fetched; the spinner has the pane until it arrives.
    public bool IsTextContentMode => HasSelection && !IsContentLoading && SelectedContentMode == NewsContentMode.Text;

    /// <summary>
    /// Text view showing HTML-looking content as native flow text.
    /// </summary>
    public bool IsFormattedTextContentMode => IsTextContentMode && HasHtmlContent;

    /// <summary>
    /// Text view showing content that carries no markup.
    /// </summary>
    public bool IsPlainTextContentMode => IsTextContentMode && !HasHtmlContent;

    public bool IsHtmlContentMode =>
        HasSelection && !IsContentLoading && SelectedContentMode == NewsContentMode.Html && HasHtmlContent;

    public bool IsWebContentMode => HasSelection && SelectedContentMode == NewsContentMode.Web && HasWebContent;

    public bool CanOpenSelected => SelectedUrlUri is not null;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _groupCatalog.InitializeAsync(cancellationToken);
        _groupCatalog.GroupsChanged += OnGroupsChanged;
        var addedGroups = ApplyGroups(_groupCatalog.CurrentGroups);

        // Sources are not fetched here: the view navigates to the first group once it is
        // loaded, and every group fetches on its first navigation instead. Every group is
        // filled from the items it stored on its last load first, so navigating to one shows
        // those while its sources are fetched again.
        await RestoreGroupsAsync(addedGroups, cancellationToken);
        _initialized = true;
    }

    public async Task SelectGroupAsync(NewsGroupViewModel group, CancellationToken cancellationToken = default)
    {
        // The list switches to the new group before anything is awaited, so the previous
        // group's items never linger on screen while this one loads. Whatever this group has
        // cached shows straight away; a group not loaded yet simply shows an empty list.
        SelectedGroup = group;
        IsBusy = true;

        // A query typed against the previous group says nothing about this one.
        CloseSearch();
        RebuildItems(group);

        await _selectionLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsStillSelected(group))
            {
                // A newer selection arrived while this one waited for the lock; it owns the list now.
                return;
            }

            IsBusy = true;
            await group.RefreshStaleAsync(cancellationToken);

            if (IsStillSelected(group))
            {
                RebuildItems(group);
            }
        }
        finally
        {
            if (IsStillSelected(group))
            {
                IsBusy = false;
            }

            _selectionLock.Release();
        }
    }

    private bool IsStillSelected(NewsGroupViewModel group) => ReferenceEquals(SelectedGroup, group);

    private bool IsContentModeAvailable(NewsContentMode mode) => mode switch
    {
        NewsContentMode.Text => HasPlainContent,
        NewsContentMode.Html => HasHtmlContent,
        NewsContentMode.Web => HasWebContent,
        _ => false
    };

    private async Task RefreshSelectedGroupAsync()
    {
        if (SelectedGroup is not { } group)
        {
            return;
        }

        await _selectionLock.WaitAsync();
        try
        {
            IsBusy = true;
            await group.RefreshAllAsync(CancellationToken.None);

            if (IsStillSelected(group))
            {
                RebuildItems(group);
            }
        }
        finally
        {
            if (IsStillSelected(group))
            {
                IsBusy = false;
            }

            _selectionLock.Release();
        }
    }

    private async Task OpenSelectedArticleAsync()
    {
        if (SelectedUrlUri is null)
        {
            return;
        }

        await Launcher.LaunchUriAsync(SelectedUrlUri);
    }

    private void CopySelectedAsText()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        var lines = new[] { item.Title, item.Url, SelectedDateTimeText, item.Lead }
            .Where(line => !string.IsNullOrWhiteSpace(line));

        SetClipboardText(string.Join(Environment.NewLine, lines));
    }

    private void CopySelectedUrl()
    {
        if (SelectedUrlUri is null)
        {
            return;
        }

        SetClipboardText(SelectedUrl);
    }

    private static void SetClipboardText(string text)
    {
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };

        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void OnGroupsChanged(object? sender, IReadOnlyList<NewsGroup> groups)
    {
        var currentSelectionTitle = SelectedGroup?.Title;
        var addedGroups = ApplyGroups(groups);

        var groupToSelect = currentSelectionTitle is null
            ? Groups.FirstOrDefault()
            : Groups.FirstOrDefault(group => string.Equals(group.Title, currentSelectionTitle, StringComparison.OrdinalIgnoreCase))
              ?? Groups.FirstOrDefault();

        if (groupToSelect is not null)
        {
            _ = RestoreAndSelectGroupAsync(addedGroups, groupToSelect);
            return;
        }

        SelectedGroup = null;
        CloseSearch();
        _items.Clear();
        DayGroups.Clear();
        SelectedItem = null;
    }

    /// <summary>
    /// Rebuilds the group list, keeping the view model of every group that is still configured
    /// so it keeps the items it has loaded. Returns the groups that were added, which are the
    /// ones holding nothing yet.
    /// </summary>
    private List<NewsGroupViewModel> ApplyGroups(IReadOnlyList<NewsGroup> groups)
    {
        var currentByGroup = Groups.ToDictionary(vm => vm.Group, vm => vm);
        var addedGroups = new List<NewsGroupViewModel>();
        Groups.Clear();

        foreach (var group in groups)
        {
            if (currentByGroup.TryGetValue(group, out var currentViewModel))
            {
                Groups.Add(currentViewModel);
                continue;
            }

            var addedGroup = new NewsGroupViewModel(
                group,
                _newsSourceClient,
                _newsItemStore,
                _loggerFactory.CreateLogger<NewsGroupViewModel>());

            Groups.Add(addedGroup);
            addedGroups.Add(addedGroup);
        }

        RefreshSelectedGroupCommand.NotifyCanExecuteChanged();
        return addedGroups;
    }

    private async Task RestoreAndSelectGroupAsync(
        IReadOnlyList<NewsGroupViewModel> groupsToRestore,
        NewsGroupViewModel groupToSelect)
    {
        await RestoreGroupsAsync(groupsToRestore, CancellationToken.None);
        await SelectGroupAsync(groupToSelect);
    }

    /// <summary>
    /// Fills each group with the items it stored on its last load. Runs before a group is
    /// navigated to, so the list it shows is the stored one rather than an empty one.
    /// </summary>
    private static async Task RestoreGroupsAsync(
        IReadOnlyList<NewsGroupViewModel> groups,
        CancellationToken cancellationToken)
    {
        foreach (var group in groups)
        {
            await group.RestoreAsync(cancellationToken);
        }
    }

    private void RebuildItems(NewsGroupViewModel newsGroup)
    {
        // Only a group merging several sources needs each row to name the one it came from.
        var showSourceName = newsGroup.HasMultipleSources;

        _items.Clear();
        _items.AddRange(newsGroup
            .GetMergedItems()
            .Select(entry => new NewsItemViewModel(entry.Item, entry.Source, showSourceName)));

        RebuildDayGroups();
    }

    /// <summary>
    /// Groups the items the search text lets through into the days the list shows, keeping the
    /// reading pane on the selected story while it is still listed and handing it the first row
    /// when it is not.
    /// </summary>
    private void RebuildDayGroups()
    {
        var selectedUrl = SelectedItem?.Url;
        var query = _appliedQuery.Length > 0 ? _appliedQuery : null;

        // The rows read this as they are built, which is what marks the matched words in them.
        foreach (var item in _items)
        {
            item.SearchQuery = query;
        }

        var groupedItems = _items
            .Where(item => query is null || item.Matches(query))
            .GroupBy(item => DateOnly.FromDateTime(item.Date.LocalDateTime.Date))
            .OrderByDescending(group => group.Key);

        DayGroups.Clear();
        NewsItemViewModel? selectedCandidate = null;

        foreach (var group in groupedItems)
        {
            var dayGroup = new NewsDayGroupViewModel(group.Key, group.OrderByDescending(item => item.Date));
            DayGroups.Add(dayGroup);

            if (selectedCandidate is null && selectedUrl is not null)
            {
                selectedCandidate = dayGroup.FirstOrDefault(item =>
                    string.Equals(item.Url, selectedUrl, StringComparison.OrdinalIgnoreCase));
            }
        }

        SelectedItem = selectedCandidate ?? DayGroups.SelectMany(group => group).FirstOrDefault();

        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>
    /// Narrows the list to <paramref name="query"/>, unless it is already the one shown — which
    /// is how a query cleared outright settles the throttled emission that follows it.
    /// </summary>
    private void ApplyQuery(string query)
    {
        if (string.Equals(_appliedQuery, query, StringComparison.Ordinal))
        {
            return;
        }

        _appliedQuery = query;
        RebuildDayGroups();
    }

    private void CloseSearch()
    {
        SearchText = string.Empty;
        IsSearchOpen = false;

        // Closing the box is a decision, not typing: the list is whole again at once rather
        // than a throttle window later.
        ApplyQuery(string.Empty);
    }

    partial void OnSelectedGroupChanged(NewsGroupViewModel? value)
    {
        RefreshSelectedGroupCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedItemChanged(NewsItemViewModel? value)
    {
        SelectedContentMode = ResolveContentMode(value);
        BeginContentLoad(value);
        RaiseSelectedItemPresentationPropertiesChanged();
    }

    /// <summary>
    /// Starts fetching the selected item's body from its own page, for the sources that name a
    /// selector. Whatever was being fetched for the previous selection is dropped.
    /// </summary>
    private void BeginContentLoad(NewsItemViewModel? item)
    {
        _contentLoad?.Cancel();
        _contentLoad?.Dispose();
        _contentLoad = null;
        IsContentLoading = false;

        // A story already fetched keeps what it has, and the web view shows the page itself, so
        // neither has anything to wait for.
        if (item is not { HasFetchedContent: true, FetchedContent: null } ||
            SelectedContentMode is NewsContentMode.Web)
        {
            return;
        }

        var load = new CancellationTokenSource();
        _contentLoad = load;
        IsContentLoading = true;

        _ = LoadContentAsync(item, load);
    }

    private async Task LoadContentAsync(NewsItemViewModel item, CancellationTokenSource load)
    {
        var selector = item.ContentSelector!;

        try
        {
            var content = await _articleContentClient.LoadAsync(item.Url, selector, load.Token);

            // A page whose selector matched nothing falls back to the body the feed carried, and
            // is marked as fetched either way so selecting the story again does not retry it.
            item.FetchedContent = content ?? item.Content ?? string.Empty;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            // A newer selection owns the pane; this body is no longer wanted.
        }
        catch (Exception exception)
        {
            // A page that cannot be fetched — a failed request, a timeout, an open circuit —
            // leaves the story on the body the feed carried, and is left unfetched so returning
            // to it tries the page again. Nothing is worth faulting this fetch-and-forget task.
            _logger.LogWarning(exception, "Failed to load the '{Selector}' content of {Url}.", selector, item.Url);
        }
        finally
        {
            if (ReferenceEquals(_contentLoad, load))
            {
                _contentLoad = null;
                IsContentLoading = false;

                // The body only arrives now, so a view that needs one is only available now.
                SelectedContentMode = ResolveContentMode(item);
                RaiseSelectedItemPresentationPropertiesChanged();
            }

            load.Dispose();
        }
    }

    private NewsContentMode ResolveContentMode(NewsItemViewModel? item)
    {
        // The source decides the view; the text view stands in when it configures none, or
        // when this item cannot show the one it asks for.
        if (item?.View is { } configured && IsContentModeAvailable(configured))
        {
            return configured;
        }

        return NewsContentMode.Text;
    }

    partial void OnSelectedContentModeChanged(NewsContentMode value)
    {
        OnPropertyChanged(nameof(IsTextContentMode));
        OnPropertyChanged(nameof(IsFormattedTextContentMode));
        OnPropertyChanged(nameof(IsPlainTextContentMode));
        OnPropertyChanged(nameof(IsHtmlContentMode));
        OnPropertyChanged(nameof(IsWebContentMode));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListLoading));
        RefreshSelectedGroupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPullRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListLoading));
    }

    partial void OnIsContentLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTextContentMode));
        OnPropertyChanged(nameof(IsFormattedTextContentMode));
        OnPropertyChanged(nameof(IsPlainTextContentMode));
        OnPropertyChanged(nameof(IsHtmlContentMode));
    }

    partial void OnIsSearchOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSearchCollapsed));
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        _searchTextInput.OnNext(value);
    }

    private void RaiseSelectedItemPresentationPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedDateTimeText));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SelectedViewsText));
        OnPropertyChanged(nameof(SelectedReactionsText));
        OnPropertyChanged(nameof(SelectedByline));
        OnPropertyChanged(nameof(SelectedUrl));
        OnPropertyChanged(nameof(SelectedUrlUri));
        OnPropertyChanged(nameof(SelectedPlainContent));
        OnPropertyChanged(nameof(SelectedHtmlContent));
        OnPropertyChanged(nameof(HasPlainContent));
        OnPropertyChanged(nameof(HasHtmlContent));
        OnPropertyChanged(nameof(HasWebContent));
        OnPropertyChanged(nameof(IsTextContentMode));
        OnPropertyChanged(nameof(IsFormattedTextContentMode));
        OnPropertyChanged(nameof(IsPlainTextContentMode));
        OnPropertyChanged(nameof(IsHtmlContentMode));
        OnPropertyChanged(nameof(IsWebContentMode));
        OnPropertyChanged(nameof(CanOpenSelected));
        OpenSelectedArticleCommand.NotifyCanExecuteChanged();
        CopySelectedAsTextCommand.NotifyCanExecuteChanged();
        CopySelectedUrlCommand.NotifyCanExecuteChanged();
    }
}
