using System.Net.Http.Headers;
using System.Text.Json;

namespace SelfCheck.Checks;

public static class Probe
{
    public const string MissingId = "selfcheck-probe";

    public static string Fill(string template) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{[^}/]+\}", MissingId);

    public static async Task<(string? Token, string Reason)> TokenAsync(
        SelfCheckConfig.AccessPassport? access, SelfCheckConfig.PassportUser? user)
    {
        if (access?.TokenEndpoint is null || access.ClientId is null || user?.Username is null)
            return (null, "блок access у selfcheck.json не заповнено");

        var password = user.PasswordEnv is null
            ? null
            : Environment.GetEnvironmentVariable(user.PasswordEnv);
        if (string.IsNullOrEmpty(password))
            return (null, $"не задано змінну середовища {user.PasswordEnv} з паролем {user.Username}");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = access.ClientId,
            ["username"] = user.Username,
            ["password"] = password,
            ["scope"] = "openid"
        });
        try
        {
            var response = await http.PostAsync(access.TokenEndpoint, form);
            if (!response.IsSuccessStatusCode)
                return (null, $"сервер ідентичності відповів {(int)response.StatusCode} для {user.Username}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.TryGetProperty("access_token", out var token)
                ? (token.GetString(), "")
                : (null, "у відповіді сервера ідентичності немає access_token");
        }
        catch (HttpRequestException ex)
        {
            return (null, $"сервер ідентичності недоступний: {ex.Message}");
        }
    }

    public static async Task<(int Code, string ContentType)> GetAsync(
        HttpClient http, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request);
        return ((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType ?? "");
    }
}
