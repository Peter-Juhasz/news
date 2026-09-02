using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using News.Client.WinUI3.Configuration;
using News.Client.WinUI3.Groups;
using News.Client.WinUI3.Services;
using News.Client.WinUI3.ViewModels;

namespace News.Client.WinUI3;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        InitializeComponent();
        _host = BuildHost();
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
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<GroupFileLoader>();
        builder.Services.AddSingleton<IGroupCatalog, GroupCatalog>();
        builder.Services.AddSingleton<INewsSourceClient, NewsSourceClient>();

        builder.Services
            .AddHttpClient<IRssFeedClient, RssFeedClient>(httpClient =>
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

        return builder.Build();
    }
}
