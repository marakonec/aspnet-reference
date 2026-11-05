using System.Net;
using System.Text;
using System.Text.Json;

namespace SelfCheck.Checks;

public static class Stage1
{
    public static async Task RunAsync(Context ctx)
    {
        var report = ctx.Report;
        var cfg = ctx.Config;

        JsonDocument? document = null;
        try
        {
            var text = await ctx.Http.GetStringAsync("/openapi/v1.json");
            document = JsonDocument.Parse(text);
            var paths = document.RootElement.GetProperty("paths").EnumerateObject().Count();
            report.Ok("1.1", $"Документ OpenAPI доступний, шляхів: {paths}");
        }
        catch (Exception ex)
        {
            report.Fail("1.1", $"Документ OpenAPI недоступний або зіпсований: {ex.Message}");
        }

        if (document is not null && cfg.KeyOperation is not null)
        {
            var template = cfg.KeyOperation.Path;
            var found = document.RootElement.GetProperty("paths")
                .EnumerateObject()
                .Any(p => PathsMatch(p.Name, template));
            if (found)
                report.Ok("1.2", $"Ключова операція є в контракті: {template}");
            else
                report.Fail("1.2", $"У контракті немає ключової операції {template}");
        }
        else
        {
            report.Skip("1.2", "Ключову операцію не названо в selfcheck.json");
        }

        if (cfg.InvalidBodyTarget is not null && cfg.InvalidBody is not null)
        {
            var body = cfg.InvalidBody.Value.GetRawText();
            using var request = new HttpRequestMessage(
                new HttpMethod(cfg.InvalidBodyTarget.Method), cfg.InvalidBodyTarget.Path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var response = await ctx.Http.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var hasFields = payload.Contains("\"errors\"", StringComparison.Ordinal);

            using var broken = new HttpRequestMessage(
                new HttpMethod(cfg.InvalidBodyTarget.Method), cfg.InvalidBodyTarget.Path)
            {
                Content = new StringContent("{ \"broken\": ", Encoding.UTF8, "application/json")
            };
            var brokenResponse = await ctx.Http.SendAsync(broken);
            var brokenType = brokenResponse.Content.Headers.ContentType?.MediaType ?? "";

            if (response.StatusCode != HttpStatusCode.BadRequest
                || contentType != "application/problem+json" || !hasFields)
                report.Fail("1.3", $"Очікували 400, application/problem+json і errors, " +
                                   $"отримали {(int)response.StatusCode} і {contentType}");
            else if (brokenResponse.StatusCode != HttpStatusCode.BadRequest
                     || brokenType != "application/problem+json")
                report.Fail("1.3", "Зіпсований JSON: очікували 400 і application/problem+json, " +
                                   $"отримали {(int)brokenResponse.StatusCode} і {brokenType}");
            else
                report.Ok("1.3", "Неправильне тіло: 400, опис проблеми, перелік полів. " +
                                 "Зіпсований JSON: 400, опис проблеми");
        }
        else
        {
            report.Skip("1.3", "Неправильне тіло не описано в selfcheck.json");
        }

        if (!string.IsNullOrWhiteSpace(cfg.UnknownResource))
        {
            var response = await ctx.Http.GetAsync(cfg.UnknownResource);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (response.StatusCode == HttpStatusCode.NotFound
                && contentType == "application/problem+json")
                report.Ok("1.4", "Неіснуючий ресурс: 404 в описі проблеми");
            else
                report.Fail("1.4", $"Очікували 404 і application/problem+json, " +
                                   $"отримали {(int)response.StatusCode} і {contentType}");
        }
        else
        {
            report.Skip("1.4", "Неіснуючий ресурс не названо в selfcheck.json");
        }

        if (cfg.Artifacts.Count == 0)
        {
            report.Skip("1.5", "Перелік документів порожній у selfcheck.json");
        }
        else
        {
            var missing = cfg.Artifacts
                .Where(a => !FileHasContent(Path.Combine(ctx.RepoRoot, a.Value)))
                .Select(a => a.Value)
                .ToList();
            if (missing.Count == 0)
                report.Ok("1.5", $"Документи на місці: {string.Join(", ", cfg.Artifacts.Values)}");
            else
                report.Fail("1.5", $"Немає або порожні: {string.Join(", ", missing)}");
        }

        document?.Dispose();
    }

    private static bool FileHasContent(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static bool PathsMatch(string documentPath, string configured)
    {
        var a = documentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var b = configured.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            var placeholder = a[i].StartsWith('{') && b[i].StartsWith('{');
            if (!placeholder && !a[i].Equals(b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
