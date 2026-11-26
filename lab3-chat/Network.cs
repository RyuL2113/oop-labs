using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Zen.Core;
namespace Zen.Chat;

public record ChatMessage(string Type, string Nick = "", string Text = "", DateTime Time = default, string? Recipient = null, string[]? Users = null)
{
    public string ToJson() => JsonSerializer.Serialize(this, Storage.Options);
    public static ChatMessage FromJson(string line) => JsonSerializer.Deserialize<ChatMessage>(line, Storage.Options) ?? throw new InvalidDataException("Пустое сообщение.");
}
public sealed class ClientHandler : IDisposable
{
    private readonly TcpClient _socket;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly object _sendLock = new();
    public string Nick { get; internal set; } = "";
    public ClientHandler(TcpClient socket)
    {
        _socket = socket;
        _reader = new StreamReader(socket.GetStream(), Encoding.UTF8);
        _writer = new StreamWriter(socket.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        socket.SendTimeout = 3000;
    }
    public async Task<ChatMessage?> ReadAsync(CancellationToken ct)
    {
        var line = await _reader.ReadLineAsync(ct);
        if (line is null)
            return null;
        if (line.Length > 65536)
            throw new InvalidDataException("Сообщение слишком большое.");
        return ChatMessage.FromJson(line);
    }
    public void Send(ChatMessage message)
    {
        lock (_sendLock)
            _writer.WriteLine(message.ToJson());
    }
    public void Dispose()
    {
        _socket.Dispose();
        _reader.Dispose();
        _writer.Dispose();
    }
}
public sealed class ChatServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly object _gate = new();
    private readonly List<ClientHandler> _clients = [];
    private readonly List<ChatMessage> _history;
    private readonly string _historyPath;
    private readonly CancellationTokenSource _stop = new();
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public ChatServer(int port, string historyPath)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _historyPath = historyPath;
        _history = File.Exists(historyPath) ? Storage.Load<List<ChatMessage>>(historyPath).Where(m => m.Type == "text" && m.Recipient == null).TakeLast(20).ToList() : [];
    }
    public async Task RunAsync()
    {
        _listener.Start();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => Handle(socket));
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }
    private async Task Handle(TcpClient socket)
    {
        using var handler = new ClientHandler(socket);
        try
        {
            using var loginTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            loginTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            var login = await handler.ReadAsync(loginTimeout.Token);
            if (login?.Type != "login" || string.IsNullOrWhiteSpace(login.Nick) || login.Nick.Trim().Length > 24)
            {
                handler.Send(new("error", Text: "Ник должен содержать от 1 до 24 символов."));
                return;
            }
            lock (_gate)
            {
                var nick = login.Nick.Trim();
                if (_clients.Any(c => c.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase)))
                {
                    handler.Send(new("error", Text: "Этот ник уже занят."));
                    return;
                }
                handler.Nick = nick;
                _clients.Add(handler);
                handler.Send(new("welcome", Nick: nick));
                foreach (var message in _history)
                    handler.Send(message with
                    {
                        Type = "history"
                    });
                Broadcast(new("system", Text: nick + " вошёл в чат", Time: DateTime.UtcNow));
                Users();
            }
            while (await handler.ReadAsync(_stop.Token) is { } incoming)
            {
                if (incoming.Type != "text" || string.IsNullOrWhiteSpace(incoming.Text) || incoming.Text.Length > 4000)
                {
                    handler.Send(new("error", Text: "Введите сообщение длиной до 4000 символов."));
                    continue;
                }
                lock (_gate)
                {
                    var message = new ChatMessage("text", handler.Nick, incoming.Text.Trim(), DateTime.UtcNow, incoming.Recipient);
                    if (incoming.Recipient != null)
                    {
                        var recipient = _clients.Find(c => c.Nick == incoming.Recipient);
                        if (recipient == null)
                            handler.Send(new("error", Text: "Адресат уже вышел."));
                        else
                        {
                            SafeSend(recipient, message);
                            if (recipient != handler)
                                SafeSend(handler, message);
                        }
                    }
                    else
                    {
                        _history.Add(message);
                        if (_history.Count > 20)
                            _history.RemoveAt(0);
                        try
                        {
                            Storage.Save(_historyPath, _history);
                        }
                        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { handler.Send(new("error", Text: "История не сохранена: " + ex.Message)); }
                        Broadcast(message);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SocketException or JsonException or OperationCanceledException or ObjectDisposedException) { }
        finally { lock (_gate) { if (_clients.Remove(handler)) { Broadcast(new("system", Text: handler.Nick + " вышел из чата", Time: DateTime.UtcNow)); Users(); } } }
    }
    private static void SafeSend(ClientHandler client, ChatMessage message)
    {
        try
        {
            client.Send(message);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SocketException or ObjectDisposedException) { }
    }
    private void Broadcast(ChatMessage message)
    {
        foreach (var client in _clients)
            SafeSend(client, message);
    }
    private void Users() => Broadcast(new("users", Users: _clients.Select(c => c.Nick).Order().ToArray()));
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        lock (_gate)
        {
            foreach (var c in _clients.ToArray())
                c.Dispose();
        }
    }
}
public sealed class ChatClient : IDisposable
{
    private ClientHandler? _connection;
    private readonly CancellationTokenSource _stop = new();
    public event Action<ChatMessage>? MessageReceived;
    public event Action<string>? Disconnected;
    public async Task ConnectAsync(string host, int port, string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            throw new ArgumentException("Введите ник.");
        var socket = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await socket.ConnectAsync(host, port, timeout.Token);
            _connection = new ClientHandler(socket);
            _connection.Send(new("login", Nick: nick));
            var reply = await _connection.ReadAsync(timeout.Token);
            if (reply?.Type != "welcome")
                throw new IOException(reply?.Text ?? "Сервер закрыл соединение.");
        }
        catch { socket.Dispose(); throw; }
    }
    public async Task ListenAsync()
    {
        var reason = "Соединение закрыто.";
        try
        {
            while (await (_connection ?? throw new InvalidOperationException("Нет соединения.")).ReadAsync(_stop.Token) is { } message)
                MessageReceived?.Invoke(message);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SocketException or JsonException or OperationCanceledException or ObjectDisposedException) { reason = ex.Message; }
        finally { if (!_stop.IsCancellationRequested) Disconnected?.Invoke(reason); }
    }
    public Task SendAsync(string text, string? recipient = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Введите сообщение.");
        return Task.Run(() => (_connection ?? throw new InvalidOperationException("Нет соединения.")).Send(new("text", Text: text, Recipient: recipient)));
    }
    public void Dispose()
    {
        _stop.Cancel();
        _connection?.Dispose();
    }
}
