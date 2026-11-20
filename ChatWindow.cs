using System.Net.Sockets;

namespace ChatApp;

internal sealed class ChatWindow : Form
{
    private readonly TextBox hostAddress = Input("Sunucu adresi", 142);
    private readonly TextBox nickname = Input("Takma ad", 142);
    private readonly NumericUpDown clientPort = Port("İstemci portu");
    private readonly NumericUpDown serverPort = Port("Sunucu portu");
    private readonly Button connect = Ui.Button("Bağlan", true);
    private readonly Button disconnect = Ui.Button("Bağlantıyı kes");
    private readonly Button send = Ui.Button("Gönder", true);
    private readonly Button start = Ui.Button("Sunucuyu başlat", true);
    private readonly Button stop = Ui.Button("Sunucuyu durdur");
    private readonly CheckBox lan = new() { Text = "Yerel ağ (LAN)", AutoSize = true, Margin = new(3, 9, 12, 0) };
    private readonly TextBox draft = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = RoomProtocol.MaxText, Margin = new(0, 4, 8, 5) };
    private readonly Label roomState = Ui.Label("Bağlantı kapalı", 11, true, Ui.Accent);
    private readonly Label identity = Ui.Label("Yazmak için bir kullanıcı adıyla bağlanın.", 10, false, Ui.Muted);
    private readonly Label hostState = Ui.Label("Sunucu duruyor", 11, true, Ui.Accent);
    private readonly Label participantCount = Ui.Label("0 katılımcı", 11, true);
    private readonly Label feedback = Ui.Label("", 10, false, Ui.Muted);
    private readonly Conversation conversation = new();
    private readonly RichTextBox activity = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.None, Margin = new(0, 8, 0, 0),
        AccessibleName = "Sunucu olay günlüğü"
    };
    private readonly ListBox peers = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, HorizontalScrollbar = true, AccessibleName = "Bağlı istemciler" };
    private RoomClient? client;
    private RoomHost? server;
    private CancellationTokenSource? connectingToken;
    private bool closing, sending, serverStopping;

    public ChatWindow()
    {
        Ui.Window(this, "Fırat Chat", 1080, 730, 670, 560);
        hostAddress.Text = "127.0.0.1";
        nickname.MaxLength = RoomProtocol.MaxName;
        nickname.PlaceholderText = "örn. Mavi";
        Ui.Input(draft, "Mesaj");
        draft.PlaceholderText = "Genel odaya mesaj yazın…";
        draft.MinimumSize = new(0, 57);
        activity.ForeColor = Ui.Text;
        Ui.Input(peers, "Bağlı istemciler");
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new(15, 8), Margin = new(0, 5, 0, 4) };
        var clientPage = new TabPage("Sohbet") { BackColor = Ui.Surface };
        var serverPage = new TabPage("Sunucu") { BackColor = Ui.Surface };
        tabs.TabPages.AddRange([clientPage, serverPage]);
        clientPage.Controls.Add(BuildClient());
        serverPage.Controls.Add(BuildServer());
        feedback.Margin = new(0, 6, 0, 1);
        var header = Ui.Label("Fırat Chat", 28, true);
        Controls.Add(Ui.Stack(18, (header, 0), (tabs, 100), (feedback, 0)));
        SizeChanged += (_, _) => feedback.MaximumSize = new(Math.Max(1, ClientSize.Width - 36), 0);

        connect.Click += async (_, _) => await Connect();
        disconnect.Click += (_, _) => { if (connectingToken is not null) connectingToken.Cancel(); else Disconnect("Bağlantı kesildi."); };
        send.Click += async (_, _) => await Send();
        draft.TextChanged += (_, _) => RefreshClient();
        draft.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await Send();
            }
        };
        start.Click += async (_, _) => await StartHost();
        stop.Click += async (_, _) => await StopHost();
        RefreshClient();
        RefreshHost();
    }

    private Control BuildClient()
    {
        var fields = Ui.Bar(Caption("Adres"), hostAddress, Caption("Port"), clientPort, Caption("Takma ad"), nickname, connect, disconnect);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        composer.ColumnStyles.Add(new(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new(SizeType.Absolute, 108));
        send.Dock = DockStyle.Fill;
        send.Margin = new(0, 4, 0, 5);
        composer.Controls.Add(draft, 0, 0);
        composer.Controls.Add(send, 1, 0);
        roomState.Margin = new(12, 7, 0, 0);
        var room = Ui.Bar(Ui.Label("Genel oda", 17, true), roomState);
        return Ui.Stack(16, (fields, 0), (room, 0), (identity, 0), (conversation, 100), (composer, 0),
            (Ui.Label("Alıcı  ·  Genel odadaki herkes    |    Enter gönderir, Shift+Enter yeni satır açar.", 9, ink: Ui.Muted), 0));
    }

    private Control BuildServer()
    {
        var fields = Ui.Bar(Caption("Dinleme portu"), serverPort, lan, start, stop);
        var columns = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = new(0, 10, 0, 0) };
        columns.ColumnStyles.Add(new(SizeType.Percent, 62));
        columns.ColumnStyles.Add(new(SizeType.Percent, 38));
        var events = Ui.Stack(12, (Ui.Label("Oda günlüğü", 15, true), 0), (activity, 100));
        var users = Ui.Stack(12, (Ui.Label("Bağlı istemciler", 15, true), 0), (participantCount, 0), (peers, 100));
        events.BackColor = users.BackColor = Ui.Background;
        events.Margin = new(0, 0, 8, 0);
        columns.Controls.Add(events, 0, 0);
        columns.Controls.Add(users, 1, 0);
        return Ui.Stack(16, (fields, 0), (hostState, 0), (columns, 100));
    }

    private static Label Caption(string title)
    {
        var label = Ui.Label(title, 9.5f, ink: Ui.Muted);
        label.Margin = new(0, 8, 5, 0);
        return label;
    }
    private static TextBox Input(string name, int width) =>
        new() { AccessibleName = name, Width = width, Margin = new(0, 6, 9, 3) };
    private static NumericUpDown Port(string name) =>
        new() { Minimum = 1024, Maximum = 65535, Value = 5056, AccessibleName = name, Width = 78, Margin = new(0, 6, 10, 3) };

    private async Task Connect()
    {
        string name = nickname.Text.Trim();
        if (!RoomProtocol.ValidName(name))
        {
            feedback.Text = "Takma ad 2–26 harf, rakam, boşluk, tire veya alt çizgiden oluşmalı.";
            nickname.Focus();
            return;
        }
        if (hostAddress.Text.Trim().Length == 0) { feedback.Text = "Sunucu adresini girin."; hostAddress.Focus(); return; }
        using var token = new CancellationTokenSource();
        connectingToken = token;
        roomState.Text = "Bağlanıyor…";
        feedback.Text = "";
        RefreshClient();
        try
        {
            var joined = await RoomClient.ConnectAsync(hostAddress.Text.Trim(), (int)clientPort.Value, name, token.Token);
            if (closing) { joined.Dispose(); return; }
            client = joined;
            conversation.Clear();
            roomState.Text = "●  Bağlı";
            identity.Text = $"Siz  ·  {joined.Name}";
            draft.Focus();
            _ = Receive(joined);
        }
        catch (OperationCanceledException)
        {
            roomState.Text = "Bağlantı kapalı";
            feedback.Text = "Bağlantı iptal edildi.";
        }
        catch (Exception ex)
        {
            roomState.Text = "Bağlanılamadı";
            feedback.Text = ex is SocketException ? "Sunucuya ulaşılamadı. Adres, port ve sunucunun durumunu kontrol edin." : ex.Message;
        }
        finally
        {
            connectingToken = null;
            if (!closing) RefreshClient();
        }
    }

    private async Task Receive(RoomClient from)
    {
        string reason = "Sunucu bağlantıyı kapattı.";
        try
        {
            while (await from.ReadAsync() is { } message && !closing && ReferenceEquals(client, from))
            {
                if (message.Kind is MessageKind.Chat or MessageKind.Notice) conversation.Add(message, from.Id);
            }
        }
        catch (Exception ex) { reason = "Bağlantı kesildi. " + ex.Message; }
        finally { if (!closing && ReferenceEquals(client, from)) Disconnect(reason); }
    }

    private async Task Send()
    {
        var current = client;
        string text = draft.Text.Trim();
        if (current is null || sending || text.Length == 0) return;
        sending = true;
        RefreshClient();
        try
        {
            await current.SendAsync(text);
            if (!closing && ReferenceEquals(client, current) && draft.Text.Trim() == text) draft.Clear();
        }
        catch (Exception ex)
        {
            if (!closing && ReferenceEquals(client, current)) Disconnect("Mesaj gönderilemedi. " + ex.Message);
        }
        finally { sending = false; if (!closing) RefreshClient(); }
    }

    private void Disconnect(string message)
    {
        var old = client;
        client = null;
        old?.Dispose();
        roomState.Text = "Bağlantı kapalı";
        identity.Text = "Yazmak için bir kullanıcı adıyla bağlanın.";
        feedback.Text = message;
        RefreshClient();
    }

    private void RefreshClient()
    {
        bool idle = client is null && connectingToken is null;
        connect.Enabled = hostAddress.Enabled = clientPort.Enabled = nickname.Enabled = idle;
        disconnect.Enabled = !idle;
        disconnect.Text = connectingToken is not null ? "İptal et" : "Bağlantıyı kes";
        draft.Enabled = client is not null;
        send.Enabled = !sending && client is not null && draft.Text.Trim().Length > 0;
    }

    private async Task StartHost()
    {
        if (server is not null || serverStopping) return;
        int port = (int)serverPort.Value;
        var room = new RoomHost(port, lan.Checked);
        room.Activity += message => UiThread(() =>
        {
            if (!ReferenceEquals(server, room)) return;
            if (activity.TextLength > 30_000) activity.Clear();
            string actor = message.Kind == MessageKind.Chat ? message.Name + " · " : "";
            activity.AppendText($"[{message.Time.ToLocalTime():HH:mm:ss}] {actor}{message.Text}{Environment.NewLine}");
        });
        room.RosterChanged += () => UiThread(() =>
        {
            if (!ReferenceEquals(server, room)) return;
            var list = room.Participants;
            peers.Items.Clear();
            peers.Items.AddRange(list.Cast<object>().ToArray());
            participantCount.Text = $"{list.Length} katılımcı";
        });
        try
        {
            room.Start();
            server = room;
            hostState.Text = $"●  Dinleniyor · {(lan.Checked ? "Yerel ağ" : "127.0.0.1")}:{port}";
            feedback.Text = "";
        }
        catch (Exception ex)
        {
            await room.StopAsync();
            if (!closing) feedback.Text = "Sunucu başlatılamadı. " + ex.Message;
        }
        if (!closing) RefreshHost();
    }

    private async Task StopHost()
    {
        var old = server;
        server = null;
        if (old is null) return;
        serverStopping = true;
        RefreshHost();
        try { await old.StopAsync(); }
        catch (Exception ex) { if (!closing) feedback.Text = "Sunucu kapatılırken: " + ex.Message; }
        finally
        {
            serverStopping = false;
            if (!closing)
            {
                participantCount.Text = "0 katılımcı";
                peers.Items.Clear();
                hostState.Text = "Sunucu duruyor";
                RefreshHost();
            }
        }
    }

    private void RefreshHost()
    {
        start.Enabled = serverPort.Enabled = lan.Enabled = server is null && !serverStopping;
        stop.Enabled = server is not null;
    }

    private void UiThread(Action action)
    {
        if (closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        closing = true;
        connectingToken?.Cancel();
        client?.Dispose();
        _ = StopHost();
        base.OnFormClosed(e);
    }
}
