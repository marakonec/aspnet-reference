using SelfCheck;
using SelfCheck.Checks;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var stage = ArgValue("--stage") is { } s && int.TryParse(s, out var parsed) ? parsed : 1;
var repoRoot = Path.GetFullPath(ArgValue("--repo") ?? ".");

if (args.Contains("--preflight"))
{
    var preflight = new ConsoleReport("Передумови");
    Console.WriteLine("Передумови робочого місця");
    Console.WriteLine(new string('─', 6));
    Preflight.Run(preflight, repoRoot);
    return preflight.Summary();
}

SelfCheckConfig config;
try
{
    config = SelfCheckConfig.Load(repoRoot);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var baseUrl = ArgValue("--base") ?? config.BaseUrl;

using var http = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(5)
};

var report = new ConsoleReport($"Стан {stage}");
var context = new Context(http, report, config, repoRoot);

Console.WriteLine($"Самоперевірка · стан {stage} · {baseUrl}");
Console.WriteLine(new string('─', 6));

string? token = null;
var tokenReason = "токен потрібен із третього стану";
if (stage >= 3)
{
    (token, tokenReason) = await Probe.TokenAsync(config.Access, config.Access?.PrivilegedUser);
    if (token is not null)
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
}

await Stage0.RunAsync(context);
if (stage >= 1) await Stage1.RunAsync(context);
if (stage >= 2) await Stage2.RunAsync(context, token);
if (stage >= 3) await Stage3.RunAsync(context, token, tokenReason);
if (stage >= 4) await Stage4.RunAsync(context);

return report.Summary();

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

namespace SelfCheck
{
    public sealed record Context(
        HttpClient Http,
        ConsoleReport Report,
        SelfCheckConfig Config,
        string RepoRoot);
}
