using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using App.Api.Persistence;
using App.Shared;

namespace App.Api.Observability;

public sealed class DatabaseHealthCheck(IServiceProvider services, FeatureSwitches switches) : IHealthCheck
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public const string Name = "database";

    public const string ReadyTag = "ready";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!switches.UsesDatabase)
            return HealthCheckResult.Healthy("Сховище в пам'яті процесу: зовнішньої бази немає");

        var db = services.GetRequiredService<AppDbContext>();
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var probe = db.Database.GetDbConnection().CreateCommand();
                probe.CommandText = "SELECT 1";
                await probe.ExecuteScalarAsync(cancellationToken);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
            return HealthCheckResult.Healthy("База відповіла на команду");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("База недоступна", ex);
        }
    }

    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            totalMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = (int)e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.GetType().Name
            })
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }

    public static HealthCheckOptions ReadyOptions() => new()
    {
        Predicate = registration => registration.Tags.Contains(ReadyTag),
        ResponseWriter = WriteResponse
    };
}
