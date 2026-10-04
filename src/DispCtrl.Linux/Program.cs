using DispCtrl.Linux.Commands;
using DispCtrl.Linux.Engine;

namespace DispCtrl.Linux;

/// <summary>
/// <c>dispctrl-linux</c>: the Linux command line and, as <c>engine</c>, the
/// resident process. Mirrors <c>dispctrl.exe</c>'s vocabulary and exit codes
/// (0 done, 1 refused, 2 asked wrongly), so the two stay a recognisable pair.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "engine")
        {
            return args.Length switch
            {
                1 => EngineHost.Run(),
                2 when args[1] == "run" => EngineHost.Run(),
                2 when args[1] is "status" or "stop" => ToEngine(args, orElse: "not running"),
                _ => Wrong("engine takes run, status or stop"),
            };
        }

        bool local = args.Contains("--local");
        if (local) args = args.Where(a => a != "--local").ToArray();

        if (!local && args.Length > 0 && EngineClient.TrySend(args) is { } reply)
        {
            Console.Out.Write(reply.Stdout);
            Console.Error.Write(reply.Stderr);
            return reply.ExitCode;
        }
        return CommandRunner.Run(args, CommandContext.Local(Console.Out, Console.Error));
    }

    private static int ToEngine(string[] args, string orElse)
    {
        if (EngineClient.TrySend(args) is not { } reply)
        {
            Console.WriteLine($"engine       {orElse}");
            return args[1] == "stop" ? CommandRunner.Refused : CommandRunner.Done;
        }
        Console.Out.Write(reply.Stdout);
        Console.Error.Write(reply.Stderr);
        return reply.ExitCode;
    }

    private static int Wrong(string message)
    {
        Console.Error.WriteLine($"dispctrl-linux: {message}");
        return CommandRunner.AskedWrongly;
    }
}
