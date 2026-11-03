using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using App.Api.Domain;
using App.Shared;

namespace App.Api.Security;

public sealed class OwnReservationRequirement : IAuthorizationRequirement;

public sealed class OwnReservationHandler(ILogger<OwnReservationHandler> log)
    : AuthorizationHandler<OwnReservationRequirement, Reservation>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnReservationRequirement requirement,
        Reservation resource)
    {
        if (context.User.IsInRole(AppAuthentication.Roles.Operator))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var customerId = context.User.FindFirst(AppAuthentication.OwnerIdClaim)?.Value;
        if (context.User.IsInRole(AppAuthentication.Roles.Customer)
            && !string.IsNullOrEmpty(customerId)
            && string.Equals(customerId, resource.CustomerId, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        log.LogDebug("Політика «тільки своє»: claim={Claim} resource={Resource}",
            customerId ?? "(немає)", resource.CustomerId);
        return Task.CompletedTask;
    }
}

public readonly record struct AccessDecision(bool Allowed, string Reason, string Detail)
{
    public static readonly AccessDecision Allow = new(true, string.Empty, string.Empty);
}

public sealed class ReservationAccess(
    IAuthorizationService authorization,
    AuthFailureCounters counters,
    ILogger<ReservationAccess> log)
{
    public async Task<AccessDecision> CheckAsync(ClaimsPrincipal user, Reservation reservation)
    {
        var result = await authorization.AuthorizeAsync(
            user, reservation, Policies.OwnReservation);

        if (result.Succeeded) return AccessDecision.Allow;

        return user.IsInRole(Roles.Customer) || user.IsInRole(Roles.Operator)
            ? Deny(AuthReasons.NotOwner,
                "Доступ до бронювання не дозволено", reservation)
            : Deny(AuthReasons.RoleMissing,
                "У токені немає жодної ролі домену", reservation);
    }

    // Для колекції рішення за ресурсом стає фільтром запиту: оператор бачить усі
    // бронювання, замовник лише свої.
    public string? VisibleOwner(ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Operator)) return null;
        return user.FindFirst(AppAuthentication.OwnerIdClaim)?.Value ?? string.Empty;
    }

    public AccessDecision CheckCreate(ClaimsPrincipal user, string? requestedOwner, out string owner)
    {
        owner = requestedOwner ?? string.Empty;
        if (user.IsInRole(Roles.Operator)) return AccessDecision.Allow;

        var own = user.FindFirst(AppAuthentication.OwnerIdClaim)?.Value;
        if (!user.IsInRole(Roles.Customer) || string.IsNullOrEmpty(own))
            return Deny(AuthReasons.RoleMissing, "У токені немає ролі замовника");
        if (!string.IsNullOrEmpty(requestedOwner) && !string.Equals(requestedOwner, own, StringComparison.Ordinal))
            return Deny(AuthReasons.NotOwner, "Замовник створює бронювання лише від власного імені");

        owner = own;
        return AccessDecision.Allow;
    }

    public AccessDecision CheckOperator(ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Operator)) return AccessDecision.Allow;

        counters.Count(AuthReasons.RoleMissing);
        log.LogWarning("{Event} reason={Reason} user={User} required={Required}",
            AppEvents.AuthorizationDenied, AuthReasons.RoleMissing,
            user.Identity?.Name ?? "-", Roles.Operator);

        return new AccessDecision(false, AuthReasons.RoleMissing,
            "Видавати прилади може лише оператор прокату");
    }

    private AccessDecision Deny(string reason, string detail)
    {
        counters.Count(reason);
        log.LogWarning("{Event} reason={Reason}", AppEvents.AuthorizationDenied, reason);
        return new AccessDecision(false, reason, detail);
    }

    private AccessDecision Deny(string reason, string detail, Reservation reservation)
    {
        counters.Count(reason);
        log.LogWarning(
            "{Event} reason={Reason} reservation={ReservationId} owner={Owner}",
            AppEvents.AuthorizationDenied, reason, reservation.Id, reservation.CustomerId);
        return new AccessDecision(false, reason, detail);
    }

    private static class Roles
    {
        public const string Operator = AppAuthentication.Roles.Operator;
        public const string Customer = AppAuthentication.Roles.Customer;
    }

    private static class Policies
    {
        public const string OwnReservation = AppAuthentication.Policies.OwnReservation;
    }
}
