namespace SelfCheck;

public enum CheckStatus { Ok, Fail, Skip }

public sealed class ConsoleReport(string title)
{
    private readonly List<(CheckStatus Status, string Id, string Text)> _lines = [];

    public void Add(CheckStatus status, string id, string text)
    {
        _lines.Add((status, id, text));
        var mark = status switch
        {
            CheckStatus.Ok => "[  OK  ]",
            CheckStatus.Fail => "[ FAIL ]",
            _ => "[ SKIP ]"
        };
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = status switch
        {
            CheckStatus.Ok => ConsoleColor.Green,
            CheckStatus.Fail => ConsoleColor.Red,
            _ => ConsoleColor.DarkGray
        };
        Console.Write(mark);
        Console.ForegroundColor = previous;
        Console.WriteLine($" {id} {text}");
    }

    public void Ok(string id, string text) => Add(CheckStatus.Ok, id, text);
    public void Fail(string id, string text) => Add(CheckStatus.Fail, id, text);
    public void Skip(string id, string text) => Add(CheckStatus.Skip, id, text);

    public int Summary()
    {
        var ok = _lines.Count(l => l.Status == CheckStatus.Ok);
        var fail = _lines.Count(l => l.Status == CheckStatus.Fail);
        var skip = _lines.Count(l => l.Status == CheckStatus.Skip);
        Console.WriteLine(new string('─', 6));
        Console.WriteLine(
            $"{title}: пройдено {ok} із {_lines.Count}, пропущено {skip}, провалено {fail}");
        return fail == 0 ? 0 : 1;
    }
}
