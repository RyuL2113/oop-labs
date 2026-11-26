using Zen.Chat;
using Zen.Core;
try
{
    var port = args.Length > 0 ? int.Parse(args[0]) : 5050;
    using var server = new ChatServer(port, Storage.DataPath("chat-history"));
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; server.Dispose(); };
    Console.WriteLine($"Zen Chat · TCP {port}. Ctrl+C — завершить.");
    await server.RunAsync();
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
