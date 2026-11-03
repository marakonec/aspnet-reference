using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SelfCheck.Checks;

public static partial class Stage3
{
    public static async Task RunAsync(Context ctx, string? privilegedToken, string tokenReason)
    {
        var report = ctx.Report;
        var cfg = ctx.Config;
        var access = cfg.Access;

        await AnonymousAsync(ctx);

        if (privilegedToken is null)
            report.Skip("3.2", $"Токена привілейованого користувача немає: {tokenReason}");
        else if (string.IsNullOrWhiteSpace(access?.OwnResource))
            report.Skip("3.2", "Ресурс access.ownResource не названо в selfcheck.json");
        else
        {
            var (code, _) = await Probe.GetAsync(ctx.Http, access.OwnResource, privilegedToken);
            if (code == 200) report.Ok("3.2", $"Дійсний токен {access.PrivilegedUser?.Username}: 200");
            else report.Fail("3.2", $"Дійсний токен {access.PrivilegedUser?.Username}: {code}");
        }

        var (ownerToken, ownerReason) = await Probe.TokenAsync(access, access?.OwnerUser);
        if (ownerToken is null)
        {
            report.Skip("3.3", $"Токена власника немає: {ownerReason}");
            report.Skip("3.4", $"Токена власника немає: {ownerReason}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(access!.ForeignResource))
                report.Skip("3.3", "Ресурс access.foreignResource не названо в selfcheck.json");
            else
            {
                var (code, type) = await Probe.GetAsync(ctx.Http, access.ForeignResource, ownerToken);
                if (code is 403 or 404 && type == "application/problem+json")
                    report.Ok("3.3", $"Власник над чужим ресурсом: {code} в описі проблеми");
                else
                    report.Fail("3.3", $"Власник над чужим ресурсом: {code} {type}, очікували 403 або 404 в описі проблеми");
            }

            if (string.IsNullOrWhiteSpace(access.OwnResource))
                report.Skip("3.4", "Ресурс access.ownResource не названо в selfcheck.json");
            else
            {
                var (code, _) = await Probe.GetAsync(ctx.Http, access.OwnResource, ownerToken);
                if (code == 200) report.Ok("3.4", "Власник над своїм ресурсом: 200");
                else report.Fail("3.4", $"Власник над своїм ресурсом: {code}");
            }
        }

        SecretScanner.Run(ctx);

        PinnedVersions(ctx);

        Checklist(ctx);
    }

    private static async Task AnonymousAsync(Context ctx)
    {
        using var anonymous = new HttpClient { BaseAddress = ctx.Http.BaseAddress, Timeout = ctx.Http.Timeout };
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(await anonymous.GetStringAsync("/openapi/v1.json"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            ctx.Report.Fail("3.1", $"Документ контракту недоступний без токена: {ex.Message}");
            return;
        }

        using (document)
        {
            var open = new List<string>();
            var noChallenge = new List<string>();
            int checkedCount = 0, declaredAnonymous = 0;
            var root = document.RootElement;
            var hasSchemes = root.TryGetProperty("components", out var components)
                             && components.TryGetProperty("securitySchemes", out _);
            var rootProtected = root.TryGetProperty("security", out var rootSecurity)
                                && rootSecurity.ValueKind == JsonValueKind.Array
                                && rootSecurity.GetArrayLength() > 0;
            foreach (var path in root.GetProperty("paths").EnumerateObject())
            {
                foreach (var operation in path.Value.EnumerateObject())
                {
                    if (!HttpMethods.Contains(operation.Name)) continue;
                    if (hasSchemes && !IsProtected(operation.Value, rootProtected))
                    {
                        declaredAnonymous++;
                        continue;
                    }

                    checkedCount++;
                    var method = new HttpMethod(operation.Name.ToUpperInvariant());
                    using var request = new HttpRequestMessage(method, Probe.Fill(path.Name));
                    if (method != HttpMethod.Get && method != HttpMethod.Delete)
                        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    using var response = await anonymous.SendAsync(request);
                    if ((int)response.StatusCode != 401)
                        open.Add($"{method} {path.Name} → {(int)response.StatusCode}");
                    else if (response.Headers.WwwAuthenticate.Count == 0)
                        noChallenge.Add($"{method} {path.Name}");
                }
            }

            if (checkedCount == 0)
                ctx.Report.Fail("3.1", "У документі контракту немає жодної операції");
            else if (open.Count > 0)
                ctx.Report.Fail("3.1", $"Без токена не 401: {string.Join("; ", open)}");
            else if (noChallenge.Count > 0)
                ctx.Report.Fail("3.1", $"Відповідь 401 без заголовка WWW-Authenticate: {string.Join("; ", noChallenge)}");
            else
                ctx.Report.Ok("3.1", $"Без токена всі {checkedCount} операцій дають 401 із WWW-Authenticate" +
                                     (declaredAnonymous > 0 ? $", явно анонімних у контракті {declaredAnonymous}" : ""));
        }
    }

    // Операція захищена, якщо має непорожній перелік security або успадковує його з кореня документа.
    private static bool IsProtected(JsonElement operation, bool rootProtected)
    {
        if (!operation.TryGetProperty("security", out var security) || security.ValueKind != JsonValueKind.Array)
            return rootProtected;
        return security.GetArrayLength() > 0;
    }

    private static readonly HashSet<string> HttpMethods =
        ["get", "put", "post", "delete", "patch", "head", "options"];

    private static void PinnedVersions(Context ctx)
    {
        var central = File.Exists(Path.Combine(ctx.RepoRoot, "Directory.Packages.props"));
        var projects = Directory.EnumerateFiles(ctx.RepoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !Stage2.IsBuildOutput(p))
            .ToList();
        var tracked = SecretScanner.Git(ctx.RepoRoot, "ls-files")?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim().Replace('\\', '/'))
            .ToHashSet();
        var withoutLock = projects
            .Where(p => File.ReadAllText(p).Contains("<PackageReference", StringComparison.Ordinal))
            .Where(p =>
            {
                var lockFile = Path.Combine(Path.GetDirectoryName(p)!, "packages.lock.json");
                var relative = Path.GetRelativePath(ctx.RepoRoot, lockFile).Replace('\\', '/');
                return !File.Exists(lockFile) || (tracked is not null && !tracked.Contains(relative));
            })
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        if (!central)
            ctx.Report.Fail("3.6", "Немає Directory.Packages.props, версії пакетів не зібрано в одному місці");
        else if (withoutLock.Count > 0)
            ctx.Report.Fail("3.6", $"Файл блокування packages.lock.json відсутній або не під контролем Git: {string.Join(", ", withoutLock)}");
        else
            ctx.Report.Ok("3.6", "Версії закріплено центральним файлом версій і файлами блокування під контролем Git");
    }

    private static void Checklist(Context ctx)
    {
        var relative = ctx.Config.Artifacts.TryGetValue("owaspChecklist", out var configured)
            ? configured
            : "docs/OWASP-CHECKLIST.md";
        var path = Path.Combine(ctx.RepoRoot, relative);
        if (!File.Exists(path))
        {
            ctx.Report.Fail("3.7", $"Немає чекліста OWASP: {relative}");
            return;
        }

        var decided = new HashSet<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var match = CategoryRegex().Match(line);
            if (!match.Success) continue;
            var cells = line.Split('|');
            if (cells.Length > 3 && !string.IsNullOrWhiteSpace(cells[3]) && !cells[3].Contains("---"))
                decided.Add(match.Value[..3]);
        }

        var missing = Enumerable.Range(1, 10).Select(i => $"A{i:00}").Where(c => !decided.Contains(c)).ToList();
        if (missing.Count == 0)
            ctx.Report.Ok("3.7", "Чекліст OWASP: для всіх десяти категорій записано рішення");
        else
            ctx.Report.Fail("3.7", $"Чекліст OWASP: немає рішення для {string.Join(", ", missing)}");
    }

    [GeneratedRegex(@"A(0[1-9]|10)(:2025)?")]
    private static partial Regex CategoryRegex();
}
