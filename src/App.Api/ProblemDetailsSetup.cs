using System.Text.Json;
using System.Text.RegularExpressions;

namespace App.Api;

public static partial class ProblemDetailsSetup
{
    // Ключі errors записуються так само, як поля в JSON контракту (customerId, items[0].count),
    // інакше клієнт не зіставить помилку з полем.
    public static void Configure(ProblemDetailsOptions options) =>
        options.CustomizeProblemDetails = context =>
        {
            if (context.ProblemDetails is not HttpValidationProblemDetails validation)
                return;

            var errors = validation.Errors.ToList();
            validation.Errors.Clear();
            foreach (var (key, messages) in errors)
                validation.Errors[ToJsonPath(key)] = messages;
        };

    private static string ToJsonPath(string key) =>
        SegmentStart().Replace(key, m => JsonNamingPolicy.CamelCase.ConvertName(m.Value));

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex SegmentStart();
}
