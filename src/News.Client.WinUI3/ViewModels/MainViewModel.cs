using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using News.Client.WinUI3.Groups;
using News.Client.WinUI3.Services;
using News.Domain;
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
    }

    public ObservableCollection<NewsGroupViewModel> Groups { get; } = [];

    public ObservableCollection<NewsDayGroupViewModel> DayGroups { get; } = [];

    public IAsyncRelayCommand RefreshSelectedGroupCommand { get; }

    public IAsyncRelayCommand OpenSelectedArticleCommand { get; }

    public IRelayCommand CopySelectedAsTextCommand { get; }

    public IRelayCommand CopySelectedUrlCommand { get; }

    [ObservableProperty]
    public partial NewsGroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial NewsItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial NewsContentMode SelectedContentMode { get; set; } = NewsContentMode.Text;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool HasSelection => SelectedItem is not null;

    public bool HasNoSelection => SelectedItem is null;

    public string SelectedTitle => SelectedItem?.Title ?? "Select a story";

    public string SelectedDateTimeText => SelectedItem?.Date.ToLocalTime().ToString("f") ?? string.Empty;

    public string SelectedSource => SelectedItem?.SourceTitle ?? SelectedUrlUri?.Host ?? string.Empty;

    public string SelectedByline => string.IsNullOrEmpty(SelectedSource)
        ? SelectedDateTimeText
        : $"{SelectedSource} · {SelectedDateTimeText}";

    public string SelectedUrl => SelectedItem?.Url ?? string.Empty;

    public Uri? SelectedUrlUri => SelectedItem?.UrlUri;

    public string SelectedPlainContent => SelectedItem?.PlainContent ?? string.Empty;

    public string? SelectedHtmlContent => SelectedItem?.HtmlContent;

    public bool HasPlainContent => !string.IsNullOrWhiteSpace(SelectedPlainContent);

    public bool HasHtmlContent => !string.IsNullOrWhiteSpace(SelectedHtmlContent);

    public bool HasWebContent => SelectedUrlUri is not null;

    public bool HasMultipleContentModes =>
        (HasPlainContent ? 1 : 0) + (HasHtmlContent ? 1 : 0) + (HasWebContent ? 1 : 0) > 1;

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
        if (!IsContentModeAvailable(mode))
        {
            return;
        }

        SelectedContentMode = mode;
    }

    private bool IsContentModeAvailable(NewsContentMode mode) => mode switch
    {
        NewsContentMode.Text => HasPlainContent,
        NewsContentMode.Html => HasHtmlContent,
        NewsContentMode.Web => HasWebContent,
        _ => false
    };

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

    private void RebuildDayGroups(IReadOnlyList<SourcedNewsItem> items)
    {
        var selectedUrl = SelectedItem?.Url;
        var groupedItems = items
            .Select(entry => new NewsItemViewModel(entry.Item, entry.Source))
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
        SelectedContentMode = ResolveInitialContentMode(value);
        RaiseSelectedItemPresentationPropertiesChanged();
    }

    private NewsContentMode ResolveInitialContentMode(NewsItemViewModel? item)
    {
        // The source's preferred view wins whenever this item can actually show it.
        if (item?.DefaultView is { } preferred && IsContentModeAvailable(preferred))
        {
            return preferred;
        }

        return HasWebContent
            ? NewsContentMode.Web
            : HasPlainContent
                ? NewsContentMode.Text
                : NewsContentMode.Html;
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

    private void RaiseSelectedItemPresentationPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedDateTimeText));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SelectedByline));
        OnPropertyChanged(nameof(SelectedUrl));
        OnPropertyChanged(nameof(SelectedUrlUri));
        OnPropertyChanged(nameof(SelectedPlainContent));
        OnPropertyChanged(nameof(SelectedHtmlContent));
        OnPropertyChanged(nameof(HasPlainContent));
        OnPropertyChanged(nameof(HasHtmlContent));
        OnPropertyChanged(nameof(HasWebContent));
        OnPropertyChanged(nameof(HasMultipleContentModes));
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
