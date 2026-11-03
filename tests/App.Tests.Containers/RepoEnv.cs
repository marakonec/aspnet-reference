namespace App.Tests;

public static class RepoEnv
{
    public static string Get(string key)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) path = Path.Combine(dir.FullName, ".env.example");
            if (!File.Exists(path)) continue;

            foreach (var line in File.ReadLines(path))
            {
                var text = line.Trim();
                if (text.StartsWith(key + "=", StringComparison.Ordinal))
                    return text[(key.Length + 1)..];
            }
            throw new InvalidOperationException($"У {path} немає ключа {key}");
        }
        throw new InvalidOperationException($"Не знайдено .env або .env.example вище за {AppContext.BaseDirectory}");
    }
}
