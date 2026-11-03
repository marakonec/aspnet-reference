using System.Text.Json;

namespace SelfCheck.Checks;

public static class Stage0
{
    public static async Task RunAsync(Context ctx)
    {
        var report = ctx.Report;

        try
        {
            var live = await ctx.Http.GetAsync("/health/live");
            if (live.IsSuccessStatusCode)
                report.Ok("0.1", "Застосунок відповідає на перевірку «живий»");
            else
                report.Fail("0.1", $"«Живий» повернув {(int)live.StatusCode}");
        }
        catch (Exception ex)
        {
            report.Fail("0.1", $"Застосунок недоступний: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(ctx.Config.UnknownResource))
        {
            report.Skip("0.2", "Неіснуючий ресурс не названо в selfcheck.json");
            return;
        }

        // Опис проблеми містить traceId поточного запиту. Якщо застосунок підхопив
        // переданий заголовок traceparent, у traceId буде той самий ідентифікатор.
        var traceId = Guid.NewGuid().ToString("n");
        using var request = new HttpRequestMessage(HttpMethod.Get, ctx.Config.UnknownResource);
        request.Headers.Add("traceparent", $"00-{traceId}-0123456789abcdef-01");
        var response = await ctx.Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        string? seen = null;
        try
        {
            using var problem = JsonDocument.Parse(body);
            if (problem.RootElement.TryGetProperty("traceId", out var t))
                seen = t.GetString();
        }
        catch (JsonException)
        {
        }

        if (seen is null)
            report.Fail("0.2", "Опис проблеми не містить traceId");
        else if (seen.Contains(traceId, StringComparison.Ordinal))
            report.Ok("0.2", "Ідентифікатор трасування із заголовка traceparent підхоплено");
        else
            report.Fail("0.2", $"Застосунок почав власне трасування ({seen}) " +
                               $"замість переданого ({traceId})");
    }
}
