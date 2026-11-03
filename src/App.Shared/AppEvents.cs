namespace App.Shared;

public static class AppEvents
{
    public const string Startup = "App.Api.Startup";

    public const string PipelineEnter = "App.Api.PipelineEnter";
    public const string PipelineExit = "App.Api.PipelineExit";

    public const string IssueRequested = "App.Api.IssueRequested";
    public const string IssueCompleted = "App.Api.IssueCompleted";
    public const string IssueRejected = "App.Api.IssueRejected";

    public const string MigrationsApplied = "App.Api.MigrationsApplied";
    public const string DatabaseSeeded = "App.Api.DatabaseSeeded";
    public const string ConcurrencyConflict = "App.Api.ConcurrencyConflict";
    public const string IssueRecordNumberTaken = "App.Api.IssueRecordNumberTaken";

    public const string TokenRejected = "App.Api.TokenRejected";
    public const string TokenAccepted = "App.Api.TokenAccepted";
    public const string AuthChallenge = "App.Api.AuthChallenge";
    public const string AuthorizationDenied = "App.Api.AuthorizationDenied";

    public const string ModelRenamed = "App.Api.ModelRenamed";
    public const string CacheEvicted = "App.Api.CacheEvicted";
    public const string RateLimitRejected = "App.Api.RateLimitRejected";
    public const string NotificationSending = "App.Api.NotificationSending";
    public const string NotificationSent = "App.Api.NotificationSent";
    public const string NotificationFailed = "App.Api.NotificationFailed";
    public const string IssueRolledBackByDependency = "App.Api.IssueRolledBackByDependency";
    public const string CircuitOpened = "App.Api.CircuitOpened";
    public const string CircuitHalfOpened = "App.Api.CircuitHalfOpened";
    public const string CircuitClosed = "App.Api.CircuitClosed";

    public const string ReadinessChecked = "App.Api.ReadinessChecked";

    public const string ValidationFailed = "App.Api.ValidationFailed";
    public const string UnhandledException = "App.Api.UnhandledException";
}
