using System.Collections.Concurrent;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("app-notifier"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .UseOtlpExporter();

var app = builder.Build();

var fault = new Fault(
    builder.Configuration.GetValue("Notifier:DelayMs", 0),
    builder.Configuration.GetValue("Notifier:FailRate", 0.0));
var delivered = new ConcurrentDictionary<string, DateTimeOffset>();
long attempts = 0;

app.MapPut("/notifications/{id}", async (string id, NotificationIn body, ILogger<Program> log,
        CancellationToken ct) =>
    {
        Interlocked.Increment(ref attempts);
        var (delayMs, failRate) = fault.Read();

        if (delayMs > 0) await Task.Delay(delayMs, ct);

        if (failRate > 0 && Random.Shared.NextDouble() < failRate)
        {
            log.LogWarning("App.Notifier.Failed id={Id} failRate={FailRate}", id, failRate);
            return Results.Problem(statusCode: 500, title: "Заглушка відмовила за перемикачем");
        }

        var first = delivered.TryAdd(id, DateTimeOffset.UtcNow);
        log.LogInformation("App.Notifier.Delivered id={Id} customer={Customer} first={First}",
            id, body.CustomerId, first);
        return Results.Ok(new { id, first });
    });

app.MapPut("/_fault", (FaultIn body) =>
{
    fault.Set(body.DelayMs, body.FailRate);
    var (delayMs, failRate) = fault.Read();
    return Results.Ok(new { delayMs, failRate });
});

app.MapGet("/_stats", () =>
{
    var (delayMs, failRate) = fault.Read();
    return Results.Ok(new
    {
        attempts = Interlocked.Read(ref attempts),
        unique = delivered.Count,
        delayMs,
        failRate
    });
});

app.MapPost("/_reset", () =>
{
    delivered.Clear();
    Interlocked.Exchange(ref attempts, 0);
    fault.Set(0, 0);
    return Results.NoContent();
});

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

app.Run();

public sealed record NotificationIn(string? ReservationId, string? CustomerId);

public sealed record FaultIn(int? DelayMs, double? FailRate);

public sealed class Fault(int delayMs, double failRate)
{
    private readonly Lock _gate = new();
    private int _delayMs = delayMs;
    private double _failRate = failRate;

    public (int DelayMs, double FailRate) Read()
    {
        lock (_gate) return (_delayMs, _failRate);
    }

    public void Set(int? delayMs, double? failRate)
    {
        lock (_gate)
        {
            if (delayMs is not null) _delayMs = Math.Max(0, delayMs.Value);
            if (failRate is not null) _failRate = Math.Clamp(failRate.Value, 0, 1);
        }
    }
}
