using Microsoft.AspNetCore.Diagnostics;
using App.Shared;

namespace App.Api;

// Зіпсований JSON або поле не того типу є помилкою клієнта. Обробник відповідає
// описом проблеми з кодом стану з винятку (400), а не 500, як для решти винятків.
public sealed class BadRequestBodyHandler(ILogger<BadRequestBodyHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad)
            return false;

        log.LogWarning("{Event} path={Path} detail={Detail}",
            AppEvents.ValidationFailed, context.Request.Path, bad.Message);

        await Results.Problem(
                title: "Тіло запиту не прочитано",
                detail: "Тіло не є коректним JSON або поле має не той тип",
                statusCode: bad.StatusCode)
            .ExecuteAsync(context);
        return true;
    }
}
