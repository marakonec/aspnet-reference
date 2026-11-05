using System.Text.RegularExpressions;

namespace SelfCheck.Checks;

public static partial class Stage4
{
    public static async Task RunAsync(Context ctx)
    {
        var report = ctx.Report;

        try
        {
            var ready = await ctx.Http.GetAsync("/health/ready");
            var body = await ready.Content.ReadAsStringAsync();
            if (ready.IsSuccessStatusCode && body.Length > 0)
                report.Ok("4.1", "Перевірка «готовий» відповідає 200");
            else
                report.Fail("4.1", $"/health/ready повернув {(int)ready.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            report.Fail("4.1", $"/health/ready недоступний: {ex.Message}");
        }

        var testsDir = Path.Combine(ctx.RepoRoot, "tests");
        var testProjects = (Directory.Exists(testsDir)
                ? Directory.EnumerateFiles(testsDir, "*.csproj", SearchOption.AllDirectories)
                : [])
            .Where(p => !Stage2.IsBuildOutput(p))
            .Select(p => (Path: p, Text: File.ReadAllText(p)))
            .Where(p => p.Text.Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal)
                        || p.Text.Contains("xunit", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var containers = testProjects.Count(p => p.Text.Contains("Testcontainers", StringComparison.Ordinal));
        if (testProjects.Count == 0)
            report.Fail("4.2", "Немає тестових проєктів у tests/");
        else if (containers == 0)
            report.Fail("4.2", $"Тестових проєктів {testProjects.Count}, але жоден не працює з контейнером бази");
        else
            report.Ok("4.2", $"Тестових проєктів {testProjects.Count}, з них із контейнером бази {containers}");

        var dockerfile = AppDockerfile(ctx);
        if (dockerfile is null)
            report.Fail("4.3", "Не знайдено Dockerfile застосунку (Dockerfile заглушки src/App.Notifier " +
                               "не враховується). Він має бути єдиним або названим у робочому процесі конвеєра");
        else
        {
            var lines = File.ReadAllLines(dockerfile).Select(l => l.Trim()).ToList();
            var stages = lines.Count(l => l.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
            var user = lines.LastOrDefault(l => l.StartsWith("USER ", StringComparison.OrdinalIgnoreCase));
            var root = user is null || RootUser().IsMatch(user);
            if (stages >= 2 && !root)
                report.Ok("4.3", $"Dockerfile: етапів {stages}, {user}");
            else
                report.Fail("4.3", $"Dockerfile: етапів {stages}, {(user ?? "USER не задано, контейнер працює від root")}");
        }

        var deploy = Path.Combine(ctx.RepoRoot, "compose.deploy.yaml");
        if (!File.Exists(deploy))
            report.Fail("4.4", "Немає compose.deploy.yaml");
        else
        {
            var text = File.ReadAllText(deploy);
            var build = BuildKey().IsMatch(text);
            var waits = text.Contains("service_healthy", StringComparison.Ordinal);
            if (!build && waits)
                report.Ok("4.4", "compose.deploy.yaml: образ із реєстру, застосунок чекає готовності бази");
            else
                report.Fail("4.4", $"compose.deploy.yaml: {(build ? "є ключ build:, образ складається локально" : "")}" +
                                   $"{(!waits ? " немає condition: service_healthy" : "")}".Trim());
        }

        Pipeline(ctx);
    }

    private static string? AppDockerfile(Context ctx)
    {
        // Dockerfile заглушки зовнішньої залежності переноситься з еталона в роботі 2
        // і не є образом застосунку студента.
        var stub = Path.Combine(ctx.RepoRoot, "src", "App.Notifier") + Path.DirectorySeparatorChar;
        var all = Directory.EnumerateFiles(ctx.RepoRoot, "Dockerfile", SearchOption.AllDirectories)
            .Where(p => !Stage2.IsBuildOutput(p))
            .Where(p => !p.StartsWith(stub, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var folder = Path.Combine(ctx.RepoRoot, ".github", "workflows");
        if (Directory.Exists(folder))
        {
            var pipeline = string.Join('\n', Directory.EnumerateFiles(folder).Select(File.ReadAllText));
            var named = all.FirstOrDefault(p => pipeline.Contains(
                Path.GetRelativePath(ctx.RepoRoot, p).Replace('\\', '/'), StringComparison.Ordinal));
            if (named is not null) return named;
        }
        return all.Count == 1 ? all[0] : null;
    }

    private static readonly (string Step, string[] Evidence)[] Steps =
    [
        ("складання", ["dotnet build"]),
        ("тести", ["dotnet test"]),
        ("лінтер контракту", ["spectral"]),
        ("порівняння контрактів", ["oasdiff"]),
        ("аудит залежностей", ["NuGetAudit", "--vulnerable", "dotnet restore"]),
        ("образ", ["docker build", "build-push-action", "docker/build"]),
        ("публікація в реєстр", ["ghcr.io", "docker push", "push: true"])
    ];

    private static void Pipeline(Context ctx)
    {
        var folder = Path.Combine(ctx.RepoRoot, ".github", "workflows");
        var files = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".yml") || f.EndsWith(".yaml")).ToList()
            : [];
        if (files.Count == 0)
        {
            ctx.Report.Fail("4.5", "Немає файла робочого процесу в .github/workflows");
            return;
        }

        var text = string.Join('\n', files.Select(File.ReadAllText));
        var missing = Steps
            .Where(g => !g.Evidence.Any(e => text.Contains(e, StringComparison.OrdinalIgnoreCase)))
            .Select(g => g.Step)
            .ToList();
        if (missing.Count == 0)
            ctx.Report.Ok("4.5", "Робочий процес містить усі сім обов'язкових кроків");
        else
            ctx.Report.Fail("4.5", $"У робочому процесі не видно кроків: {string.Join(", ", missing)}");
    }

    [GeneratedRegex(@"^USER\s+(root|0)(\s|:|$)", RegexOptions.IgnoreCase)]
    private static partial Regex RootUser();

    [GeneratedRegex(@"^\s*build\s*:", RegexOptions.Multiline)]
    private static partial Regex BuildKey();
}
