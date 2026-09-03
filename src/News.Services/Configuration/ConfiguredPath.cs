namespace News.Configuration;

/// <summary>
/// Turns the paths configuration carries into full ones: environment variables are expanded, and
/// a relative path is taken from the folder the app runs out of.
/// </summary>
public static class ConfiguredPath
{
    public static string Resolve(string configuredPath)
    {
        configuredPath = Environment.ExpandEnvironmentVariables(configuredPath);

        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }
}
