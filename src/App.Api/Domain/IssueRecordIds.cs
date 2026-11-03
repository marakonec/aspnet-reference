namespace App.Api.Domain;

public static class IssueRecordIds
{
    public static string Next(int issuedCount) => $"iss-{issuedCount + 1}";
}
