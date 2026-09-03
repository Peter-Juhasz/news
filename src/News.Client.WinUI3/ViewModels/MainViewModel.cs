using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using News.Domain;
using News.Groups;
using News.Services;
using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace News.Client.WinUI3.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IGroupCatalog _groupCatalog;
    private readonly INewsSourceClient _newsSourceClient;
    private readonly IArticleContentClient _articleContentClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainViewModel> _logger;
    private readonly SemaphoreSlim _selectionLock = new(1, 1);

    /// <summary>
    /// Every row of the selected group, before the search text narrows it down. Held so typing
    /// re-groups this list instead of merging the group's sources again on each keystroke.
    /// </summary>
    private readonly List<NewsItemViewModel> _items = [];

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
        ILoggerFactory loggerFactory)
    {
        _groupCatalog = groupCatalog;
        _newsSourceClient = newsSourceClient;
        _articleContentClient = articleContentClient;
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
    /// A search that hides every row: the list is empty for a reason worth spelling out.
    /// </summary>
    public bool HasNoMatches => HasSearchText && DayGroups.Count == 0;

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
        ApplyGroups(_groupCatalog.CurrentGroups);
        _initialized = true;

        // Sources are not fetched here: the view navigates to the first group once it is
        // loaded, and every group fetches on its first navigation instead.
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
        ApplyGroups(groups);

        var groupToSelect = currentSelectionTitle is null
            ? Groups.FirstOrDefault()
            : Groups.FirstOrDefault(group => string.Equals(group.Title, currentSelectionTitle, StringComparison.OrdinalIgnoreCase))
              ?? Groups.FirstOrDefault();

        if (groupToSelect is not null)
        {
            _ = SelectGroupAsync(groupToSelect);
            return;
        }

        SelectedGroup = null;
        CloseSearch();
        _items.Clear();
        DayGroups.Clear();
        SelectedItem = null;
    }

    private void ApplyGroups(IReadOnlyList<NewsGroup> groups)
    {
        var currentByGroup = Groups.ToDictionary(vm => vm.Group, vm => vm);
        Groups.Clear();

        foreach (var group in groups)
        {
            if (currentByGroup.TryGetValue(group, out var currentViewModel))
            {
                Groups.Add(currentViewModel);
                continue;
            }

            Groups.Add(new NewsGroupViewModel(group, _newsSourceClient, _loggerFactory.CreateLogger<NewsGroupViewModel>()));
        }

        RefreshSelectedGroupCommand.NotifyCanExecuteChanged();
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
        var query = HasSearchText ? SearchText.Trim() : null;
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

    private void CloseSearch()
    {
        SearchText = string.Empty;
        IsSearchOpen = false;
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
        RefreshSelectedGroupCommand.NotifyCanExecuteChanged();
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
        RebuildDayGroups();
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
