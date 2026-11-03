using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using App.Api.Notifications;

namespace App.Tests;

public abstract class TestHostBase : WebApplicationFactory<Program>
{
    protected abstract IReadOnlyDictionary<string, string?> Settings();

    public HttpClient ClientFor(string? user)
    {
        var client = CreateClient();
        if (user is not null) client.DefaultRequestHeaders.Add(TestUsers.Header, user);
        return client;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>();
        var file = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddEnvironmentVariables("APPTEST_")
            .Build();
        foreach (var (key, value) in file.AsEnumerable())
            if (value is not null) settings[key] = value;
        foreach (var (key, value) in Settings())
            settings[key] = value;

        builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Служба сповіщень у тестах замінена відповіддю 200, тести не залежать від заглушки в контейнері.
            services.AddHttpClient<NotifierClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new NotifierStubHandler());
        });

    private sealed class NotifierStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
