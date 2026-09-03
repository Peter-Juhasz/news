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
    private bool _isWebViewReady;
    private bool _isBrowserWebViewReady;
    private bool _isSyncingNavigationSelection;
    private bool _isSyncingListSelection;
    private Uri? _browserNavigatedUri;
    private NewsItemViewModel? _contextTargetItem;

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
        if (_isSyncingListSelection)
        {
            return;
        }

        ViewModel.SelectedItem = NewsListView.SelectedItem as NewsItemViewModel;
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
