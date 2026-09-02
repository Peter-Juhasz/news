using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using News.Client.WinUI3.ViewModels;

namespace News.Client.WinUI3;

public sealed partial class MainWindow : Window
{
    private bool _isWebViewReady;
    private bool _isSyncingNavigationSelection;

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

        if (GroupsNavigationView.SelectedItem is null && ViewModel.Groups.Count > 0)
        {
            GroupsNavigationView.SelectedItem = ViewModel.Groups[0];
        }

        SyncNavigationSelection();
        SyncContentModeSelector();
        UpdateWebViewContent();
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
        ViewModel.SelectedItem = NewsListView.SelectedItem as NewsItemViewModel;
    }

    private void ContentModeSelectorSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem selectedItem)
        {
            return;
        }

        if (string.Equals(selectedItem.Tag as string, "Html", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.SetContentMode(NewsContentMode.Html);
            return;
        }

        ViewModel.SetContentMode(NewsContentMode.Text);
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedGroup))
        {
            SyncNavigationSelection();
        }

        if (e.PropertyName is nameof(MainViewModel.SelectedContentMode) or nameof(MainViewModel.HasBothContentModes))
        {
            SyncContentModeSelector();
        }

        if (e.PropertyName is nameof(MainViewModel.SelectedHtmlContent) or nameof(MainViewModel.IsHtmlContentMode))
        {
            UpdateWebViewContent();
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

    private void SyncContentModeSelector()
    {
        ContentModeSelector.SelectedItem = ViewModel.SelectedContentMode == NewsContentMode.Html
            ? HtmlModeSelectorItem
            : TextModeSelectorItem;
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
    }

    private async Task EnsureWebViewReadyAsync()
    {
        await ArticleWebView.EnsureCoreWebView2Async();
        _isWebViewReady = true;
    }
}
