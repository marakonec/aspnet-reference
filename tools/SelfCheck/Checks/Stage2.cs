using System.Net;
using System.Text;

namespace SelfCheck.Checks;

public static class Stage2
{
    public static async Task RunAsync(Context ctx, string? token)
    {
        var report = ctx.Report;
        var cfg = ctx.Config;

        if (!string.IsNullOrWhiteSpace(cfg.VolatileResource))
        {
            var (code, type) = await Probe.GetAsync(ctx.Http, cfg.VolatileResource, token);
            if (code == 200 && type.Contains("json", StringComparison.OrdinalIgnoreCase))
                report.Ok("2.1", $"Некешований ресурс читається з бази: {cfg.VolatileResource}");
            else
                report.Fail("2.1", $"{cfg.VolatileResource} повернув {code} {type}. Чи запущено базу і чи є в ній дані?");
        }
        else
        {
            report.Skip("2.1", "Некешований ресурс (volatileResource) не названо в selfcheck.json");
        }

        var migrations = Directory.EnumerateDirectories(ctx.RepoRoot, "Migrations", SearchOption.AllDirectories)
            .Where(d => !IsBuildOutput(d))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs"))
            .Count(f => !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal)
                        && !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        if (migrations > 0)
            report.Ok("2.2", $"Міграції в репозиторії: {migrations}");
        else
            report.Fail("2.2", "У репозиторії немає теки Migrations з міграціями");

        await CacheAsync(ctx);

        if (cfg.KeyOperation is null)
            report.Skip("2.4", "Ключову операцію не названо в selfcheck.json");
        else
            await RateLimitAsync(ctx, token);
    }

    // Вихідний кеш додає до відповіді з кешу заголовок Age. Запит без токена,
    // бо політика кешу за замовчуванням не кешує автентифіковані запити.
    private static async Task CacheAsync(Context ctx)
    {
        var cfg = ctx.Config;
        if (string.IsNullOrWhiteSpace(cfg.CacheableResource))
        {
            ctx.Report.Skip("2.3", "Кешований ресурс (cacheableResource) не названо в selfcheck.json");
            return;
        }

        using var anonymous = new HttpClient { BaseAddress = ctx.Http.BaseAddress, Timeout = ctx.Http.Timeout };
        using var first = await anonymous.GetAsync(cfg.CacheableResource);
        using var second = await anonymous.GetAsync(cfg.CacheableResource);
        if (!second.IsSuccessStatusCode)
            ctx.Report.Fail("2.3", $"{cfg.CacheableResource} без токена повернув {(int)second.StatusCode}");
        else if (second.Headers.Age is not null)
            ctx.Report.Ok("2.3", $"Повторне читання {cfg.CacheableResource} віддано з кешу (Age: {second.Headers.Age.Value.TotalSeconds:F0} с)");
        else
            ctx.Report.Fail("2.3", $"Повторне читання {cfg.CacheableResource} без заголовка Age, вихідний кеш не спрацював");
    }

    private static async Task RateLimitAsync(Context ctx, string? token)
    {
        var cfg = ctx.Config;
        var burst = cfg.RateLimitBurst ?? 40;
        var path = Probe.Fill(cfg.KeyOperation!.Path);
        var body = cfg.KeyOperationBody?.GetRawText() ?? "{}";

        int rejected = 0, withRetry = 0;
        for (var i = 0; i < burst; i++)
        {
            using var request = new HttpRequestMessage(new HttpMethod(cfg.KeyOperation.Method), path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (token is not null)
                request.Headers.Authorization = new("Bearer", token);
            using var response = await ctx.Http.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.TooManyRequests) continue;
            rejected++;
            if (response.Headers.RetryAfter is not null) withRetry++;
        }

        if (rejected == 0)
            ctx.Report.Fail("2.4", $"{burst} запитів поспіль на {path}, жодної відмови 429");
        else if (withRetry < rejected)
            ctx.Report.Fail("2.4", $"Відмов 429: {rejected}, але заголовок Retry-After є лише в {withRetry}");
        else
            ctx.Report.Ok("2.4", $"Обмежувач: {rejected} відмов 429 із {burst}, у кожній є Retry-After");
    }

    internal static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p is "bin" or "obj" or ".git" or "node_modules");
    }
}
