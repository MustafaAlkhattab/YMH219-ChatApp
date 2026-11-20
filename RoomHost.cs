using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace ChatApp;

internal sealed class RoomHost
{
    public const int Limit = 24;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<TcpClient, Task> clients = new();
    private readonly Dictionary<Guid, Peer> participants = [];
    private readonly object roster = new();
    private readonly SemaphoreSlim sendQueue = new(1, 1);
    private Task? accept;

    public RoomHost(int port, bool lan) => listener = new(lan ? IPAddress.Any : IPAddress.Loopback, port);
    public event Action<RoomMessage>? Activity;
    public event Action? RosterChanged;

    public Participant[] Participants
    {
        get { lock (roster) return participants.Values.Where(p => p.Ready).Select(p => p.Info).OrderBy(p => p.Name).ToArray(); }
    }

    public void Start()
    {
        listener.Start(Limit);
        accept = AcceptAsync(stop.Token);
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (clients.Count >= Limit) { socket.Dispose(); continue; }
                socket.NoDelay = true;
                Task work = JoinAsync(socket, token);
                clients[socket] = work;
                _ = work.ContinueWith(done =>
                {
                    clients.TryRemove(socket, out _);
                    if (done.Exception is { } failure && !token.IsCancellationRequested)
                        Activity?.Invoke(new(MessageKind.Error, Text: failure.GetBaseException().Message, Time: DateTimeOffset.Now));
                }, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException ex) when (!token.IsCancellationRequested)
        {
            Activity?.Invoke(new(MessageKind.Error, Text: "Sunucu dinleme hatası: " + ex.Message, Time: DateTimeOffset.Now));
        }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
    }

    private async Task JoinAsync(TcpClient socket, CancellationToken token)
    {
        Peer? peer = null;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshake.CancelAfter(TimeSpan.FromSeconds(8));
            using var stream = socket.GetStream();
            var hello = await RoomProtocol.ReadAsync(stream, handshake.Token).ConfigureAwait(false);
            if (hello is not { Kind: MessageKind.Hello }) return;
            string name = hello.Name.Trim();
            if (!RoomProtocol.ValidName(name))
            {
                await RoomProtocol.WriteAsync(stream, new(MessageKind.Error, Text: "Geçerli bir kullanıcı adı girin."), handshake.Token).ConfigureAwait(false);
                return;
            }
            lock (roster)
            {
                if (!participants.Values.Any(p => p.Info.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    var info = new Participant(Guid.NewGuid(), name, socket.Client.RemoteEndPoint?.ToString() ?? "—", DateTimeOffset.Now);
                    peer = new(socket, info);
                    participants.Add(info.Id, peer);
                }
            }
            if (peer is null)
            {
                await RoomProtocol.WriteAsync(stream, new(MessageKind.Error, Text: "Bu ad odada kullanılıyor. Başka bir ad seçin."), handshake.Token).ConfigureAwait(false);
                return;
            }
            await RoomProtocol.WriteAsync(stream, new(MessageKind.Welcome, peer.Info.Id, name, Time: DateTimeOffset.Now), handshake.Token).ConfigureAwait(false);
            lock (roster) peer.Ready = true;
            RosterChanged?.Invoke();
            await PublishAsync(new(MessageKind.Notice, Text: $"{name} odaya katıldı.", Time: DateTimeOffset.Now), token).ConfigureAwait(false);

            while (await RoomProtocol.ReadAsync(stream, token).ConfigureAwait(false) is { } message)
            {
                string text = message.Text.Trim();
                if (message.Kind != MessageKind.Chat || text.Length is < 1 or > RoomProtocol.MaxText) continue;
                await PublishAsync(new(MessageKind.Chat, peer.Info.Id, name, text, DateTimeOffset.Now), token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            socket.Dispose();
            if (peer is not null)
            {
                lock (roster) participants.Remove(peer.Info.Id);
                RosterChanged?.Invoke();
                if (peer.Ready && !token.IsCancellationRequested)
                {
                    try { await PublishAsync(new(MessageKind.Notice, Text: $"{peer.Info.Name} odadan ayrıldı.", Time: DateTimeOffset.Now), token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                }
            }
        }
    }

    private async Task PublishAsync(RoomMessage message, CancellationToken token)
    {
        await sendQueue.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Peer[] targets;
            lock (roster) targets = participants.Values.Where(p => p.Ready).ToArray();
            Activity?.Invoke(message);
            await Task.WhenAll(targets.Select(peer => DeliverAsync(peer, message, token))).ConfigureAwait(false);
        }
        finally { sendQueue.Release(); }
    }

    private static async Task DeliverAsync(Peer peer, RoomMessage message, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try { await RoomProtocol.WriteAsync(peer.Socket.GetStream(), message, timeout.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        { peer.Socket.Dispose(); }
    }

    public async Task StopAsync()
    {
        stop.Cancel();
        listener.Stop();
        if (accept is not null) await accept.ConfigureAwait(false);
        var remaining = clients.ToArray();
        foreach (var entry in remaining) entry.Key.Dispose();
        await Task.WhenAll(remaining.Select(entry => entry.Value)).ConfigureAwait(false);
        stop.Dispose();
        sendQueue.Dispose();
    }

    private sealed record Peer(TcpClient Socket, Participant Info)
    {
        public bool Ready { get; set; }
    }
}
