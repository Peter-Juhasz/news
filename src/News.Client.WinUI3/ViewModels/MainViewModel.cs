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
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _selectionLock = new(1, 1);

    /// <summary>
    /// Every row of the selected group, before the search text narrows it down. Held so typing
    /// re-groups this list instead of merging the group's sources again on each keystroke.
    /// </summary>
    private readonly List<NewsItemViewModel> _items = [];

    private bool _initialized;

    public MainViewModel(
        IGroupCatalog groupCatalog,
        INewsSourceClient newsSourceClient,
        ILoggerFactory loggerFactory)
    {
        _groupCatalog = groupCatalog;
        _newsSourceClient = newsSourceClient;
        _loggerFactory = loggerFactory;

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

    public bool IsTextContentMode => HasSelection && SelectedContentMode == NewsContentMode.Text;

    /// <summary>
    /// Text view showing HTML-looking content as native flow text.
    /// </summary>
    public bool IsFormattedTextContentMode => IsTextContentMode && HasHtmlContent;

    /// <summary>
    /// Text view showing content that carries no markup.
    /// </summary>
    public bool IsPlainTextContentMode => IsTextContentMode && !HasHtmlContent;

    public bool IsHtmlContentMode => HasSelection && SelectedContentMode == NewsContentMode.Html && HasHtmlContent;

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
        RaiseSelectedItemPresentationPropertiesChanged();
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
