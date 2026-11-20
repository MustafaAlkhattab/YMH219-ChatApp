using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
namespace ChatApp;

// Satır tabanlı UTF-8 protokolü: ilk satır kullanıcı adı, sonraki satırlar mesaj.
public sealed class ChatServer : IDisposable
{
    private TcpListener? listener;
    private CancellationTokenSource? cancellation;
    private readonly ConcurrentDictionary<TcpClient, Peer> peers = new();
    public event Action<string>? Message;
    public event Action<string[]>? ClientsChanged;
    private sealed record Peer(string Name, StreamWriter Writer)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
    public void Start(int port, bool lan)
    {
        listener = new(lan ? IPAddress.Any : IPAddress.Loopback, port);
        listener.Start(); cancellation = new();
        _ = AcceptLoop(cancellation.Token);
        Message?.Invoke($"Sunucu {port} portunda dinliyor.");
    }
    private async Task AcceptLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener!.AcceptTcpClientAsync(token);
                _ = Handle(client, token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { if (!token.IsCancellationRequested) Message?.Invoke("Sunucu: " + ex.Message); }
    }
    private async Task Handle(TcpClient client, CancellationToken token)
    {
        string? name = null;
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token); handshake.CancelAfter(TimeSpan.FromSeconds(15));
            name = (await reader.ReadLineAsync(handshake.Token))?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 30) return;
            peers[client] = new(name, writer); NotifyClients();
            await Broadcast($"{name} sohbete katıldı.");
            while (await reader.ReadLineAsync(token) is { } text)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Length > 2000) { await writer.WriteLineAsync("Mesaj en fazla 2000 karakter olabilir."); continue; }
                await Broadcast($"{name}: {text}");
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            if (peers.TryRemove(client, out _)) { NotifyClients(); await Broadcast($"{name} ayrıldı."); }
            client.Dispose();
        }
    }
    public async Task Broadcast(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}"; Message?.Invoke(line);
        foreach (var pair in peers.ToArray())
        {
            var peer = pair.Value;
            await peer.Gate.WaitAsync();
            try { await peer.Writer.WriteLineAsync(line); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { pair.Key.Dispose(); }
            finally { peer.Gate.Release(); }
        }
    }
    private void NotifyClients() => ClientsChanged?.Invoke(peers.Select(p => $"{p.Value.Name} — {p.Key.Client.RemoteEndPoint}").ToArray());
    public void Dispose()
    {
        cancellation?.Cancel(); listener?.Stop();
        foreach (var peer in peers.Keys) peer.Dispose();
        peers.Clear(); cancellation?.Dispose(); cancellation = null; listener = null;
    }
}

public sealed class ChatClient : IDisposable
{
    private TcpClient? socket;
    private StreamWriter? writer;
    public event Action<string>? Message;
    public event Action? Disconnected;
    public async Task Connect(string host, int port, string name)
    {
        var next = new TcpClient();
        try { await next.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(8)); }
        catch { next.Dispose(); throw; }
        socket = next;
        writer = new(socket.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync(name); _ = Receive(next);
    }
    private async Task Receive(TcpClient connection)
    {
        try
        {
            using var reader = new StreamReader(connection.GetStream(), Encoding.UTF8);
            while (await reader.ReadLineAsync() is { } line) Message?.Invoke(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { Message?.Invoke("Bağlantı kapandı: " + ex.Message); }
        finally { connection.Dispose(); if (ReferenceEquals(socket, connection)) { socket = null; writer = null; Disconnected?.Invoke(); } }
    }
    public Task Send(string text) => writer?.WriteLineAsync(text) ?? throw new InvalidOperationException("Önce bağlanın.");
    public void Dispose() { socket?.Dispose(); writer = null; socket = null; }
}
