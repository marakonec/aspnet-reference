namespace App.Shared;

public static class AppEvents
{
    public const string Startup = "App.Api.Startup";

    public const string PipelineEnter = "App.Api.PipelineEnter";
    public const string PipelineExit = "App.Api.PipelineExit";

    public const string IssueRequested = "App.Api.IssueRequested";
    public const string IssueCompleted = "App.Api.IssueCompleted";
    public const string IssueRejected = "App.Api.IssueRejected";

    public const string ValidationFailed = "App.Api.ValidationFailed";
    public const string UnhandledException = "App.Api.UnhandledException";
}
