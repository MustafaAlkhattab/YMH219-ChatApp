using System.Net.Sockets;

namespace ChatApp;

internal sealed class RoomClient : IDisposable
{
    private readonly TcpClient socket;
    private readonly NetworkStream stream;
    public Guid Id { get; }
    public string Name { get; }

    private RoomClient(TcpClient socket, RoomMessage welcome)
    {
        this.socket = socket;
        stream = socket.GetStream();
        Id = welcome.Sender;
        Name = welcome.Name;
    }

    public static async Task<RoomClient> ConnectAsync(string host, int port, string name, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var socket = new TcpClient { NoDelay = true };
        try
        {
            await socket.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            var stream = socket.GetStream();
            await RoomProtocol.WriteAsync(stream, new(MessageKind.Hello, Name: name), timeout.Token).ConfigureAwait(false);
            var answer = await RoomProtocol.ReadAsync(stream, timeout.Token).ConfigureAwait(false);
            if (answer?.Kind == MessageKind.Error) throw new IOException(answer.Text);
            if (answer is not { Kind: MessageKind.Welcome } || answer.Sender == Guid.Empty)
                throw new IOException("Sunucu oturum açmadı. İki pencerede de güncel uygulamayı kullanın.");
            return new(socket, answer);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            socket.Dispose();
            throw new IOException("Bağlantı zaman aşımına uğradı. Adres, port ve sunucuyu kontrol edin.");
        }
        catch { socket.Dispose(); throw; }
    }

    public Task<RoomMessage?> ReadAsync() => RoomProtocol.ReadAsync(stream, CancellationToken.None);
    public Task SendAsync(string message) =>
        RoomProtocol.WriteAsync(stream, new(MessageKind.Chat, Text: message), CancellationToken.None);
    public void Dispose() => socket.Dispose();
}
