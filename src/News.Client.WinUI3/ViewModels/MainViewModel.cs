using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using News.Client.WinUI3.Groups;
using News.Client.WinUI3.Services;
using News.Domain;
using System.Collections.ObjectModel;
using Windows.System;

namespace News.Client.WinUI3.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IGroupCatalog _groupCatalog;
    private readonly INewsSourceClient _newsSourceClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _selectionLock = new(1, 1);
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
    }

    public ObservableCollection<NewsGroupViewModel> Groups { get; } = [];

    public ObservableCollection<NewsDayGroupViewModel> DayGroups { get; } = [];

    public IAsyncRelayCommand RefreshSelectedGroupCommand { get; }

    public IAsyncRelayCommand OpenSelectedArticleCommand { get; }

    [ObservableProperty]
    public partial NewsGroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial NewsItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial NewsContentMode SelectedContentMode { get; set; } = NewsContentMode.Text;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool HasSelection => SelectedItem is not null;

    public string SelectedTitle => SelectedItem?.Title ?? "Select a story";

    public string SelectedDateTimeText => SelectedItem?.Date.ToLocalTime().ToString("f") ?? string.Empty;

    public string SelectedUrl => SelectedItem?.Url ?? string.Empty;

    public Uri? SelectedUrlUri => SelectedItem?.UrlUri;

    public string SelectedPlainContent => SelectedItem?.PlainContent ?? string.Empty;

    public string? SelectedHtmlContent => SelectedItem?.HtmlContent;

    public bool HasPlainContent => !string.IsNullOrWhiteSpace(SelectedPlainContent);

    public bool HasHtmlContent => !string.IsNullOrWhiteSpace(SelectedHtmlContent);

    public bool HasBothContentModes => HasPlainContent && HasHtmlContent;

    public bool IsTextContentMode => HasSelection && SelectedContentMode == NewsContentMode.Text;

    public bool IsHtmlContentMode => HasSelection && SelectedContentMode == NewsContentMode.Html && HasHtmlContent;

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

        if (Groups.Count > 0)
        {
            await SelectGroupAsync(Groups[0], cancellationToken);
        }
    }

    public async Task SelectGroupAsync(NewsGroupViewModel group, CancellationToken cancellationToken = default)
    {
        await _selectionLock.WaitAsync(cancellationToken);
        try
        {
            SelectedGroup = group;
            IsBusy = true;
            await group.RefreshStaleAsync(cancellationToken);
            RebuildDayGroups(group.GetMergedItems());
        }
        finally
        {
            IsBusy = false;
            _selectionLock.Release();
        }
    }

    public void SetContentMode(NewsContentMode mode)
    {
        if (mode == NewsContentMode.Html && !HasHtmlContent)
        {
            return;
        }

        if (mode == NewsContentMode.Text && !HasPlainContent)
        {
            return;
        }

        SelectedContentMode = mode;
    }

    private async Task RefreshSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        await _selectionLock.WaitAsync();
        try
        {
            IsBusy = true;
            await SelectedGroup.RefreshAllAsync(CancellationToken.None);
            RebuildDayGroups(SelectedGroup.GetMergedItems());
        }
        finally
        {
            IsBusy = false;
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

    private void RebuildDayGroups(IReadOnlyList<NewsItem> items)
    {
        var selectedUrl = SelectedItem?.Url;
        var groupedItems = items
            .Select(item => new NewsItemViewModel(item))
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
    }

    partial void OnSelectedGroupChanged(NewsGroupViewModel? value)
    {
        RefreshSelectedGroupCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedItemChanged(NewsItemViewModel? value)
    {
        SelectedContentMode = HasPlainContent ? NewsContentMode.Text : NewsContentMode.Html;
        RaiseSelectedItemPresentationPropertiesChanged();
    }

    partial void OnSelectedContentModeChanged(NewsContentMode value)
    {
        OnPropertyChanged(nameof(IsTextContentMode));
        OnPropertyChanged(nameof(IsHtmlContentMode));
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshSelectedGroupCommand.NotifyCanExecuteChanged();
    }

    private void RaiseSelectedItemPresentationPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedDateTimeText));
        OnPropertyChanged(nameof(SelectedUrl));
        OnPropertyChanged(nameof(SelectedUrlUri));
        OnPropertyChanged(nameof(SelectedPlainContent));
        OnPropertyChanged(nameof(SelectedHtmlContent));
        OnPropertyChanged(nameof(HasPlainContent));
        OnPropertyChanged(nameof(HasHtmlContent));
        OnPropertyChanged(nameof(HasBothContentModes));
        OnPropertyChanged(nameof(IsTextContentMode));
        OnPropertyChanged(nameof(IsHtmlContentMode));
        OnPropertyChanged(nameof(CanOpenSelected));
        OpenSelectedArticleCommand.NotifyCanExecuteChanged();
    }
}
