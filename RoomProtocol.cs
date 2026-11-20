using System.Buffers.Binary;
using System.Text.Json;

namespace ChatApp;

internal enum MessageKind { Hello, Welcome, Chat, Notice, Error }

internal sealed record RoomMessage(MessageKind Kind, Guid Sender = default, string Name = "", string Text = "", DateTimeOffset Time = default);
internal sealed record Participant(Guid Id, string Name, string Endpoint, DateTimeOffset Joined)
{
    public override string ToString() => $"{Name}   ·   {Endpoint}   ·   {Joined:HH:mm}";
}

internal static class RoomProtocol
{
    public const int MaxName = 26;
    public const int MaxText = 1600;
    private const int FrameLimit = 16384;

    public static bool ValidName(string value) =>
        value.Length is >= 2 and <= MaxName && value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_');

    public static async Task<RoomMessage?> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] size = new byte[4];
        if (await stream.ReadAsync(size.AsMemory(0, 1), token).ConfigureAwait(false) == 0) return null;
        await stream.ReadExactlyAsync(size.AsMemory(1), token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(size);
        if (length is <= 0 or > FrameLimit) throw new InvalidDataException("Geçersiz sohbet paketi.");
        byte[] data = new byte[length];
        await stream.ReadExactlyAsync(data, token).ConfigureAwait(false);
        try
        {
            var message = JsonSerializer.Deserialize<RoomMessage>(data);
            if (message is null || message.Name is null || message.Text is null || !Enum.IsDefined(message.Kind))
                throw new InvalidDataException("Sohbet paketi okunamadı.");
            return message;
        }
        catch (JsonException ex) { throw new InvalidDataException("Sohbet paketi okunamadı.", ex); }
    }

    public static async Task WriteAsync(Stream stream, RoomMessage message, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message);
        if (data.Length > FrameLimit) throw new InvalidDataException("Mesaj çok uzun.");
        byte[] frame = new byte[4 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, data.Length);
        data.CopyTo(frame, 4);
        await stream.WriteAsync(frame, token).ConfigureAwait(false);
    }
}
