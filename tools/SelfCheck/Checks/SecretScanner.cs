using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SelfCheck.Checks;

public static partial class SecretScanner
{
    private sealed record Rule(string Id, Regex Pattern, string Title);

    private static readonly Rule[] Rules =
    [
        new("S1", PrivateKey(), "закритий ключ"),
        new("S2", Jwt(), "токен доступу (JWT)"),
        new("S3", GitHubToken(), "токен GitHub"),
        new("S4", RealmCredential(), "пароль користувача в експорті області"),
        new("S5", LiteralPassword(), "пароль літералом")
    ];

    public static void Run(Context ctx)
    {
        var allow = LoadAllowList(ctx.RepoRoot, out var badAllow);
        if (badAllow.Count > 0)
        {
            ctx.Report.Fail("3.5", $"У .selfcheck-allow рядки без обґрунтування: {string.Join("; ", badAllow)}");
            return;
        }

        var tracked = Git(ctx.RepoRoot, "ls-files");
        var files = tracked is null
            ? Directory.EnumerateFiles(ctx.RepoRoot, "*", SearchOption.AllDirectories)
                .Where(f => !Stage2.IsBuildOutput(f))
                .Select(f => Path.GetRelativePath(ctx.RepoRoot, f))
            : tracked.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var findings = new List<string>();
        foreach (var relative in files)
        {
            var normalized = relative.Replace('\\', '/');
            var full = Path.Combine(ctx.RepoRoot, relative);
            if (!File.Exists(full) || new FileInfo(full).Length > 1_000_000) continue;
            var lineNumber = 0;
            foreach (var line in File.ReadLines(full))
            {
                lineNumber++;
                foreach (var rule in Rules)
                {
                    if (!Matches(rule, normalized, line) || IsAllowed(allow, normalized, rule.Id)) continue;
                    findings.Add($"{normalized}:{lineNumber} {rule.Title} ({rule.Id})");
                }
            }
        }

        var history = Git(ctx.RepoRoot, "log --branches -p --no-color --unified=0");
        var historyFindings = 0;
        if (history is not null)
        {
            string? file = null;
            foreach (var line in history.Split('\n'))
            {
                if (line.StartsWith("+++ b/", StringComparison.Ordinal)) { file = line[6..]; continue; }
                if (!line.StartsWith('+') || line.StartsWith("+++", StringComparison.Ordinal) || file is null) continue;
                foreach (var rule in Rules)
                {
                    if (!Matches(rule, file, line) || IsAllowed(allow, file, rule.Id)) continue;
                    historyFindings++;
                    if (historyFindings <= 5) findings.Add($"історія: {file} {rule.Title} ({rule.Id})");
                }
            }
        }

        if (findings.Count == 0)
            ctx.Report.Ok("3.5", history is null
                ? "Секретів у робочому дереві не знайдено, історію не перевірено, бо Git недоступний"
                : "Секретів не знайдено ні в робочому дереві, ні в історії");
        else
            ctx.Report.Fail("3.5", $"Схоже на секрети: {string.Join("; ", findings.Take(8))}" +
                                   (findings.Count > 8 ? $" і ще {findings.Count - 8}" : ""));
    }

    private static readonly HashSet<string> CodeExtensions =
        [".cs", ".ps1", ".psm1", ".js", ".ts", ".py", ".java", ".go", ".sh"];

    private static bool Matches(Rule rule, string file, string line) =>
        rule.Id == "S5" && CodeExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())
            ? QuotedPassword().IsMatch(line)
            : rule.Pattern.IsMatch(line);

    private static List<(string Glob, string Rule)> LoadAllowList(string root, out List<string> bad)
    {
        bad = [];
        var result = new List<(string, string)>();
        var path = Path.Combine(root, ".selfcheck-allow");
        if (!File.Exists(path)) return result;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || parts[2].Length < 10) { bad.Add(line); continue; }
            result.Add((parts[0].Replace('\\', '/'), parts[1]));
        }
        return result;
    }

    private static bool IsAllowed(List<(string Glob, string Rule)> allow, string file, string ruleId) =>
        allow.Any(a => a.Rule == ruleId && GlobMatch(a.Glob, file));

    private static bool GlobMatch(string glob, string file)
    {
        var pattern = "^" + Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$";
        return Regex.IsMatch(file, pattern);
    }

    internal static string? Git(string root, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", $"-C \"{root}\" {arguments}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"""type""\s*:\s*""password""")]
    private static partial Regex RealmCredential();

    [GeneratedRegex(@"(?i)(?<![A-Za-z])(password|passwd|pwd|client_secret|secret)[""']?\s*[:=]\s*[""']?(?![$<{*\[(])(?>[^\s""';(\[]{4,})(?![\[(])")]
    private static partial Regex LiteralPassword();

    [GeneratedRegex(@"(?i)(?<![A-Za-z])(password|passwd|pwd|client_secret|secret)[""']?\s*[:=]\s*[""'](?![$<{*])[^""'\s]{4,}[""']")]
    private static partial Regex QuotedPassword();
}
