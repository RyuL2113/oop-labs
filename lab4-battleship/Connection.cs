using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace Zen.Battleship;

public record Packet(string Type, Position Position = default, ShotReport? Report = null);
public sealed class Connection : IDisposable
{
    private readonly TcpClient _socket;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _send = new(1);
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource<ShotReport>? _pending;
    public event Action<Packet>? Received;
    public event Action<string>? Disconnected;
    private Connection(TcpClient socket)
    {
        _socket = socket;
        _reader = new(socket.GetStream(), Encoding.UTF8);
        _writer = new(socket.GetStream(), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }
    public static async Task<Connection> HostAsync(int port, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        try
        {
            return new(await listener.AcceptTcpClientAsync(ct));
        }
        finally { listener.Stop(); }
    }
    public static async Task<Connection> JoinAsync(string host, int port, CancellationToken ct)
    {
        var socket = new TcpClient();
        try
        {
            await socket.ConnectAsync(host, port, ct);
            return new(socket);
        }
        catch { socket.Dispose(); throw; }
    }
    public async Task SendAsync(Packet packet)
    {
        await _send.WaitAsync(_stop.Token);
        try
        {
            await _writer.WriteLineAsync(JsonSerializer.Serialize(packet, Core.Storage.Options));
        }
        finally { _send.Release(); }
    }
    public async Task<ShotReport> ShootAsync(Position p)
    {
        if (_pending != null)
            throw new RuleException("Дождитесь ответа.");
        var pending = new TaskCompletionSource<ShotReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        try
        {
            await SendAsync(new("shot", p));
            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), _stop.Token);
        }
        catch (TimeoutException) { Dispose(); throw new IOException("Соперник не ответил. Соединение закрыто."); }
        finally { _pending = null; }
    }
    public async Task ListenAsync()
    {
        var reason = "Соперник отключился.";
        try
        {
            while (await _reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (line.Length > 8192)
                    throw new IOException("Слишком большой пакет.");
                var packet = JsonSerializer.Deserialize<Packet>(line, Core.Storage.Options) ?? throw new IOException("Пустой пакет.");
                if (packet.Type == "result" && packet.Report != null)
                    _pending?.TrySetResult(packet.Report);
                else
                    Received?.Invoke(packet);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SocketException or JsonException or OperationCanceledException or ObjectDisposedException) { reason = ex.Message; }
        finally { _pending?.TrySetException(new IOException(reason)); if (!_stop.IsCancellationRequested) Disconnected?.Invoke(reason); }
    }
    public void Dispose()
    {
        _stop.Cancel();
        _socket.Dispose();
    }
}
public sealed class NetworkPlayer(Connection connection) : Player
{
    public override Task<ShotReport> ReceiveShotAsync(Position p) => connection.ShootAsync(p);
}
