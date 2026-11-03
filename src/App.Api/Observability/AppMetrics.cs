using System.Diagnostics.Metrics;

namespace App.Api.Observability;

public sealed class AppMetrics
{
    private readonly Counter<long> _issues;

    public AppMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.ServiceName);
        _issues = meter.CreateCounter<long>(
            "app.issues",
            unit: "{issue}",
            description: "Спроби ключової операції видачі за результатом");
    }

    public void Issue(string outcome) =>
        _issues.Add(1, new KeyValuePair<string, object?>("app.outcome", outcome));
}
