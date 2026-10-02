using DispCtrl.Control;

// Console front end. The engine owns the resident work; everything a script
// wants to ask for is here, in a process that blocks the shell and returns a
// meaningful exit code. Every command, the old short verbs included, goes
// through the control terminal and so through the same service the app and
// the engine's broker use.
if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
{
    string? topic = args.Length > 1 ? args[1] : null;
    if (ControlTerminal.HelpFor(topic) is { } page) { Console.WriteLine(page); return 0; }
    Console.Error.WriteLine($"No help topic called {topic}.");
    Console.WriteLine(ControlTerminal.HelpFor(null));
    return 2;
}

if (args[0] is "displays" && args.Length == 1) args = ["displays", "list"];
if (ControlTerminal.Handles(args)) return await ControlTerminal.RunAsync(args);

Console.Error.WriteLine($"Unknown command: {args[0]}. 'dispctrl help' lists them.");
return 2;
