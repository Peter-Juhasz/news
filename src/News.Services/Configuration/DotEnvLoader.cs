using System.Collections.ObjectModel;

namespace News.Configuration;

public static class DotEnvLoader
{
    public static IReadOnlyDictionary<string, string?> Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ReadOnlyDictionary<string, string?>(
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].Trim();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 1)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');
            if (key.Length == 0)
            {
                continue;
            }

            values[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        }

        return new ReadOnlyDictionary<string, string?>(values);
    }
}
