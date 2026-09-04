using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using News.Client.WinUI3.ViewModels;
using Windows.System;

// Windows.System carries a DispatcherQueuePriority of its own; this is the one the WinUI
// dispatcher takes.
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace News.Client.WinUI3;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// How far down the list still counts as being at the top of it. A hair above zero, so a
    /// list resting at the top after an inertial scroll is not read as having been scrolled.
    /// </summary>
    private const double TopOfListThreshold = 1;

    private bool _isWebViewReady;
    private bool _isBrowserWebViewReady;
    private bool _isSyncingNavigationSelection;
    private bool _isSyncingListSelection;
    private Uri? _browserNavigatedUri;
    private NewsItemViewModel? _contextTargetItem;
    private ScrollViewer? _newsScrollViewer;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ConfigureWindowChrome();
        GroupedNewsSource.Source = ViewModel.DayGroups;
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    public MainViewModel ViewModel { get; }

    private async void RootGridLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureWebViewReadyAsync();
        await ViewModel.InitializeAsync();

        UpdateWebViewContent();

        // Selecting the first group raises SelectionChanged, which is what fetches its
        // sources. Nothing is loaded until that first navigation happens.
        if (GroupsNavigationView.SelectedItem is null && ViewModel.Groups.Count > 0)
        {
            GroupsNavigationView.SelectedItem = ViewModel.Groups[0];
        }

        WatchNewsScrolling();
    }

    private async void GroupsNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSyncingNavigationSelection || args.IsSettingsSelected)
        {
            return;
        }

        if (args.SelectedItem is not NewsGroupViewModel selectedGroup)
        {
            return;
        }

        if (ReferenceEquals(selectedGroup, ViewModel.SelectedGroup))
        {
            return;
        }

        await ViewModel.SelectGroupAsync(selectedGroup);
    }

    private void NewsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A list taking rows in and out drops its own selection along the way and says so, which
        // is the list keeping up rather than the reader choosing. The view model settles the
        // selection itself once it has finished, and says so again.
        if (_isSyncingListSelection || ViewModel.IsUpdatingList)
        {
            return;
        }

        ViewModel.SelectedItem = NewsListView.SelectedItem as NewsItemViewModel;
    }

    private void NewsListViewLoaded(object sender, RoutedEventArgs e) => WatchNewsScrolling();

    /// <summary>
    /// Starts following where the reader is in the list. Called again once the first group is
    /// in, for the case where the list had no template to find its scroller in yet.
    /// </summary>
    private void WatchNewsScrolling()
    {
        if (_newsScrollViewer is not null)
        {
            return;
        }

        _newsScrollViewer = FindDescendant<ScrollViewer>(NewsListView);
        if (_newsScrollViewer is null)
        {
            return;
        }

        _newsScrollViewer.ViewChanged += NewsScrollViewChanged;
        UpdateItemsUpdatingScrollMode();
    }

    private void NewsScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) =>
        UpdateItemsUpdatingScrollMode();

    /// <summary>
    /// Decides what the list does with the reader's place when rows arrive above it. A reader at
    /// the top is left at the top, so the stories that came in are the first thing they see. A
    /// reader who has scrolled down keeps the row they were on, rather than having it pushed
    /// away by whatever arrived over it.
    /// </summary>
    private void UpdateItemsUpdatingScrollMode()
    {
        if (_newsScrollViewer is null || NewsListView.ItemsPanelRoot is not ItemsStackPanel itemsPanel)
        {
            return;
        }

        itemsPanel.ItemsUpdatingScrollMode = _newsScrollViewer.VerticalOffset <= TopOfListThreshold
            ? ItemsUpdatingScrollMode.KeepScrollOffset
            : ItemsUpdatingScrollMode.KeepItemsInView;
    }

    private void SyncListSelection()
    {
        var selectedItem = ViewModel.SelectedItem;
        if (ReferenceEquals(NewsListView.SelectedItem, selectedItem))
        {
            return;
        }

        _isSyncingListSelection = true;
        try
        {
            NewsListView.SelectedItem = selectedItem;
        }
        finally
        {
            _isSyncingListSelection = false;
        }
    }

    private async void NewsRefreshRequested(RefreshContainer sender, RefreshRequestedEventArgs args)
    {
        // The deferral holds the visualizer in its refreshing state until the group is back,
        // so the pull reads as the wait it started rather than snapping back on release.
        var deferral = args.GetDeferral();
        ViewModel.IsPullRefreshing = true;
        try
        {
            if (ViewModel.RefreshSelectedGroupCommand.CanExecute(null))
            {
                await ViewModel.RefreshSelectedGroupCommand.ExecuteAsync(null);
            }
        }
        finally
        {
            ViewModel.IsPullRefreshing = false;
            deferral.Complete();
        }
    }

    private void NewsListViewRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        _contextTargetItem = (e.OriginalSource as FrameworkElement)?.DataContext as NewsItemViewModel;
    }

    private void CopyAsTextAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ViewModel.CopySelectedAsTextCommand.CanExecute(null))
        {
            ViewModel.CopySelectedAsTextCommand.Execute(null);
        }
    }

    private void SearchButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsSearchOpen = true;

        // This click leaves focus on a button that the same change collapses, and the focus
        // manager rehomes focus off a collapsed element after the current batch of work. Low
        // priority puts the box's claim on focus after that, rather than under it — and after
        // the layout pass that gives the box a template to focus into.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusSearchBox);
    }

    /// <summary>
    /// Focuses the search box's templated text box. <see cref="AutoSuggestBox"/> only forwards
    /// <see cref="UIElement.Focus"/> to that part once its template is applied, so going
    /// straight to the part avoids the call quietly doing nothing.
    /// </summary>
    private void FocusSearchBox()
    {
        SearchBox.UpdateLayout();

        if (FindDescendant<TextBox>(SearchBox) is { } textBox)
        {
            textBox.Focus(FocusState.Programmatic);
            return;
        }

        SearchBox.Focus(FocusState.Programmatic);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void SearchBoxLostFocus(object sender, RoutedEventArgs e)
    {
        // An empty box earns none of the room it takes from the list.
        if (!ViewModel.HasSearchText)
        {
            ViewModel.IsSearchOpen = false;
        }
    }

    private void SearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        ViewModel.CloseSearchCommand.Execute(null);
        NewsListView.Focus(FocusState.Programmatic);
    }

    private void NewsContextFlyoutOpening(object? sender, object e)
    {
        // The menu acts on the selected item, so a right-tap on a different row moves the
        // selection first. A keyboard-invoked menu has no target and keeps the selection.
        if (_contextTargetItem is { } item)
        {
            NewsListView.SelectedItem = item;
            _contextTargetItem = null;
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedGroup))
        {
            SyncNavigationSelection();
        }

        if (e.PropertyName is nameof(MainViewModel.SelectedItem))
        {
            SyncListSelection();
        }

        if (e.PropertyName is nameof(MainViewModel.SelectedHtmlContent) or nameof(MainViewModel.IsHtmlContentMode))
        {
            UpdateWebViewContent();
        }

        if (e.PropertyName is nameof(MainViewModel.SelectedUrlUri) or nameof(MainViewModel.IsWebContentMode))
        {
            UpdateBrowserContent();
        }
    }

    private void SyncNavigationSelection()
    {
        var selectedGroup = ViewModel.SelectedGroup;
        if (ReferenceEquals(GroupsNavigationView.SelectedItem, selectedGroup))
        {
            return;
        }

        _isSyncingNavigationSelection = true;
        try
        {
            GroupsNavigationView.SelectedItem = selectedGroup;
        }
        finally
        {
            _isSyncingNavigationSelection = false;
        }
    }

    private void UpdateWebViewContent()
    {
        if (!_isWebViewReady)
        {
            return;
        }

        if (!ViewModel.IsHtmlContentMode || string.IsNullOrWhiteSpace(ViewModel.SelectedHtmlContent))
        {
            ArticleWebView.NavigateToString("<html><body></body></html>");
            return;
        }

        ArticleWebView.NavigateToString(ViewModel.SelectedHtmlContent);
    }

    private async void UpdateBrowserContent()
    {
        // The browser view is created on first use so startup does not pay for a second
        // WebView2 environment that most sessions never open.
        if (!ViewModel.IsWebContentMode)
        {
            return;
        }

        var targetUri = ViewModel.SelectedUrlUri;
        if (targetUri is null || targetUri == _browserNavigatedUri)
        {
            return;
        }

        _browserNavigatedUri = targetUri;

        if (!_isBrowserWebViewReady)
        {
            await BrowserWebView.EnsureCoreWebView2Async();
            _isBrowserWebViewReady = true;

            if (targetUri != _browserNavigatedUri)
            {
                // A newer request came in while the environment was being created.
                return;
            }
        }

        BrowserWebView.Source = targetUri;
    }

    private void ConfigureWindowChrome()
    {
        Title = "News";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            };
        }

		AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
	}

    private async Task EnsureWebViewReadyAsync()
    {
        await ArticleWebView.EnsureCoreWebView2Async();
        _isWebViewReady = true;
    }
}
