using System.Net;
using System.Net.Http.Json;
using App.Api.Contracts;
using Xunit;

namespace App.Tests;

public sealed class IssueTests(TestHost host) : IClassFixture<TestHost>
{
    [Fact]
    public async Task Operator_issues_reservation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.ClientFor(TestUsers.Operator);

        using var response = await client.PostAsync("/reservations/res-1/issue", null, ct);

        if (response.StatusCode != HttpStatusCode.OK)
            Assert.Fail($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");

        var left = await client.GetFromJsonAsync<AvailabilityDto>(
            "/models/mdl-osc/availability", ct);
        Assert.NotNull(left);
        Assert.Equal(2, left.AvailableCount);
    }

    [Fact]
    public async Task Customer_cannot_read_foreign_reservation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.ClientFor(TestUsers.CustomerOfRes2);

        using var response = await client.GetAsync("/reservations/res-1", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.ClientFor(null);

        using var response = await client.PostAsync("/reservations/res-2/issue", null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_lists_only_own_reservations()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.ClientFor(TestUsers.CustomerOfRes2);

        var reservations = await client.GetFromJsonAsync<List<ReservationDto>>("/reservations", ct);

        Assert.NotNull(reservations);
        Assert.NotEmpty(reservations);
        Assert.All(reservations, r => Assert.Equal("cus-2", r.CustomerId));
    }

    [Fact]
    public async Task Customer_cannot_create_reservation_for_another_customer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.ClientFor(TestUsers.CustomerOfRes2);
        var request = new CreateReservationRequest("cus-1", [new ReservationItemDto("mdl-osc", 1)]);

        using var response = await client.PostAsJsonAsync("/reservations", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_creates_reservation_for_self()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = host.ClientFor(TestUsers.CustomerOfRes2);
        var request = new CreateReservationRequest(null, [new ReservationItemDto("mdl-osc", 1)]);

        using var response = await client.PostAsJsonAsync("/reservations", request, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ReservationDto>(ct);
        Assert.Equal("cus-2", created?.CustomerId);
    }
}
