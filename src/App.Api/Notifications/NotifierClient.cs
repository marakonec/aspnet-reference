using System.Diagnostics;
using Polly;
using App.Api.Observability;
using App.Shared;

namespace App.Api.Notifications;

public sealed record Notification(string IssueRecordId, string ReservationId, string CustomerId, string? Phone = null);

public sealed class NotifierUnavailableException(string reason, Exception inner)
    : Exception(reason, inner);

public sealed class NotifierClient(HttpClient http, ILogger<NotifierClient> log)
{
    public async Task SendAsync(Notification notification, CancellationToken ct)
    {
        using var activity = Telemetry.Source.StartActivity("notify customer");
        activity?.SetTag("app.issue_record", notification.IssueRecordId);

        log.LogInformation("{Event} record={RecordId} customer={CustomerId} phone={Phone}",
            AppEvents.NotificationSending, notification.IssueRecordId, notification.CustomerId,
            SensitiveData.Phone(notification.Phone));

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await http.PutAsJsonAsync(
                $"/notifications/{Uri.EscapeDataString(notification.IssueRecordId)}", notification, ct);
            response.EnsureSuccessStatusCode();

            log.LogInformation("{Event} record={RecordId} status={Status} elapsedMs={ElapsedMs}",
                AppEvents.NotificationSent, notification.IssueRecordId,
                (int)response.StatusCode, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or ExecutionRejectedException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            var elapsed = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            log.LogWarning("{Event} record={RecordId} reason={Reason} elapsedMs={ElapsedMs}",
                AppEvents.NotificationFailed, notification.IssueRecordId, ex.GetType().Name, elapsed);
            throw new NotifierUnavailableException(ex.GetType().Name, ex);
        }
    }
}
