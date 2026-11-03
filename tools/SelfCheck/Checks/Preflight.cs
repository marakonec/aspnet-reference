using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SelfCheck.Checks;

public static class Preflight
{
    private static readonly int[] Ports = [15100, 15110, 15200, 15300, 18888, 18889, 18890];

    public static void Run(ConsoleReport report, string repoRoot)
    {
        var sdk = Execute("dotnet", "--version");
        if (sdk.Code == 0 && sdk.Output.StartsWith("10.", StringComparison.Ordinal))
            report.Ok("П.1", $"Пакет розробника .NET {sdk.Output}");
        else
            report.Fail("П.1", $"Потрібен пакет розробника .NET 10, знайдено «{sdk.Output}»");

        var docker = Execute("docker", "version --format {{.Server.Version}}");
        if (docker.Code == 0 && docker.Output.Length > 0)
            report.Ok("П.2", $"Docker відповідає, рушій {docker.Output}");
        else
            report.Fail("П.2", "Docker не відповідає. Запустити Docker Desktop і дочекатися рушія");

        var git = Execute("git", "--version");
        if (git.Code == 0)
            report.Ok("П.3", git.Output);
        else
            report.Fail("П.3", "Git не знайдено");

        var drive = new DriveInfo(Path.GetPathRoot(repoRoot) ?? repoRoot);
        var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
        if (freeGb >= 5)
            report.Ok("П.4", $"Вільного місця на диску {freeGb:F0} ГБ");
        else
            report.Fail("П.4", $"Вільного місця на диску {freeGb:F1} ГБ, потрібно щонайменше 5 ГБ");

        var busy = Ports.Where(p => !IsFree(p)).ToList();
        if (busy.Count == 0)
            report.Ok("П.5", $"Порти курсу вільні: {string.Join(", ", Ports)}");
        else
            report.Skip("П.5", $"Зайняті порти: {string.Join(", ", busy)}. " +
                               "Це нормально, якщо їх займають застосунок або контейнери курсу");
    }

    private static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static (int Code, string Output) Execute(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null) return (-1, "");
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000)) return (-1, "");
            return (process.ExitCode, output);
        }
        catch (Exception)
        {
            return (-1, "");
        }
    }
}
