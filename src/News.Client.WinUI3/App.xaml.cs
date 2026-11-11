using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using News.Bookmarks;
using News.Client.WinUI3.Configuration;
using News.Client.WinUI3.ViewModels;
using News.Configuration;

using News.Groups;
using News.Services;
using News.Storage;
using PeterJuhasz.Repositories.FileSystem;

namespace News.Client.WinUI3;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        InitializeComponent();
        _host = BuildHost();
        ApplyTheme();
    }

    /// <summary>
    /// Puts the configured theme on the application rather than on the window's content.
    /// </summary>
    /// <remarks>
    /// A theme set on an element reaches that element's tree and nothing else, which leaves out
    /// everything drawn beside it: the window's caption buttons, and dialogs and flyouts, which hang
    /// off the root of the tree rather than off the element that opened them. Set here it is what
    /// the whole app is drawn in, whatever part of it is being drawn. It is set before any window
    /// exists, which is the only time it can be: the application's theme is fixed once there is
    /// content drawn in it.
    /// </remarks>
    private void ApplyTheme()
    {
        var theme = _host.Services.GetRequiredService<IOptions<AppearanceOptions>>().Value.Theme;

        // ApplicationTheme has no "Default", and needs none: following the system is what the
        // application does when nothing is said, so saying nothing is how that is asked for.
        if (theme is ElementTheme.Light or ElementTheme.Dark)
        {
            RequestedTheme = theme is ElementTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Activate();
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        var dotEnvValues = DotEnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

        builder.Configuration.Sources.Clear();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        if (dotEnvValues.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(dotEnvValues);
        }

        builder.Configuration.AddEnvironmentVariables();

        builder.Services.Configure<NewsReaderOptions>(builder.Configuration.GetSection(NewsReaderOptions.SectionName));

        // The app's own look, kept apart from the reader's settings: it binds
        // Microsoft.UI.Xaml.ElementTheme, a WinUI type News.Services cannot take a dependency on.
        builder.Services.Configure<AppearanceOptions>(builder.Configuration.GetSection(AppearanceOptions.SectionName));

        builder.Services.Configure<XOptions>(builder.Configuration.GetSection(XOptions.SectionName));

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<GroupsFileLoader>();
        builder.Services.AddSingleton<IGroupCatalog, GroupCatalog>();
        builder.Services.AddSingleton<INewsSourceClient, NewsSourceClient>();
        builder.Services.AddSingleton<INewsItemStore>(static provider => new NewsItemStore(
            new DirectoryInfo(ConfiguredPath.Resolve(provider.GetRequiredService<IOptions<NewsReaderOptions>>().Value.CacheDirectoryPath)).GetPartition(),
            provider.GetRequiredService<ILogger<NewsItemStore>>()));
        builder.Services.AddSingleton<IBookmarkStore>(static provider =>
        {
            var file = new FileInfo(ConfiguredPath.Resolve(provider.GetRequiredService<IOptions<NewsReaderOptions>>().Value.BookmarksFilePath));
            return new BookmarkStore(
                file.Directory!.GetPartition(),
                file.Name,
                provider.GetRequiredService<ILogger<BookmarkStore>>());
        });
        builder.Services.AddSingleton<IBookmarkService, BookmarkService>();

        AddFeedClient(builder.Services.AddHttpClient<IRssFeedClient, RssFeedClient>());
        AddFeedClient(builder.Services.AddHttpClient<IAtomFeedClient, AtomFeedClient>());
        AddFeedClient(builder.Services.AddHttpClient<IGitHubIssueClient, GitHubIssueClient>());
        AddFeedClient(builder.Services.AddHttpClient<IHtmlPageClient, HtmlPageClient>());

        // Signed outside the resilience handler, so the request for the app's token is given the
        // same patience as the ones it signs, and a missing key fails at once rather than retried.
        builder.Services.AddSingleton<XAccessTokenCache>();
        builder.Services.AddTransient<XAuthenticationHandler>();
        AddFeedClient(builder.Services.AddHttpClient<IXClient, XClient>().AddHttpMessageHandler<XAuthenticationHandler>());

        // the providers the news source client hands each source to, one per provider library
        builder.Services.AddSingleton<INewsSourceProvider, RssNewsSourceProvider>();
        builder.Services.AddSingleton<INewsSourceProvider, AtomNewsSourceProvider>();
        builder.Services.AddSingleton<INewsSourceProvider, GitHubNewsSourceProvider>();
        builder.Services.AddSingleton<INewsSourceProvider, HtmlNewsSourceProvider>();
        builder.Services.AddSingleton<INewsSourceProvider, XNewsSourceProvider>();

        // Article pages are heavier than a feed and are fetched while the reader waits, so this
        // one gives an attempt longer to finish and retries it fewer times.
        builder.Services
            .AddHttpClient<IArticleContentClient, ArticleContentClient>(httpClient =>
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("News.Client.WinUI3/1.0");
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html, application/xhtml+xml");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.Retry.MaxRetryAttempts = 2;
            });

        return builder.Build();
    }

    /// <summary>
    /// The RSS, Atom, GitHub, HTML page and X clients fetch alike, so they are given the same client
    /// settings and the same patience with a slow or failing feed. GitHub's API turns away a
    /// request without the user agent set here.
    /// </summary>
    private static void AddFeedClient(IHttpClientBuilder builder) =>
        builder
            .ConfigureHttpClient(httpClient =>
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("News.Client.WinUI3/1.0");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.Retry.MaxRetryAttempts = 3;
            });
}
