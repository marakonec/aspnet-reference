using Testcontainers.PostgreSql;
using Xunit;

namespace App.Tests;

public sealed class TestHost : TestHostBase, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db =
        new PostgreSqlBuilder(RepoEnv.Get("APP_IMG_DB")).Build();

    public async ValueTask InitializeAsync() =>
        await _db.StartAsync(TestContext.Current.CancellationToken);

    protected override IReadOnlyDictionary<string, string?> Settings() =>
        new Dictionary<string, string?>
        {
            ["Features:Persistence"] = "postgres",
            ["ConnectionStrings:Default"] = _db.GetConnectionString()
        };

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}
