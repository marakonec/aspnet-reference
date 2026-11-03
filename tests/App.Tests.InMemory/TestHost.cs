namespace App.Tests;

public sealed class TestHost : TestHostBase
{
    protected override IReadOnlyDictionary<string, string?> Settings() =>
        new Dictionary<string, string?>
        {
            ["Features:Persistence"] = "memory"
        };
}
