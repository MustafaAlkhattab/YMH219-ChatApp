namespace ChatApp;

public partial class Form1 : Form
{
    private readonly ChatServer server = new();
    private readonly ChatClient client = new();
    private readonly TextBox host = new() { Text = "127.0.0.1", Width = 140, AccessibleName = "Sunucu adresi" };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 5055, Width = 85, AccessibleName = "İstemci portu" };
    private readonly TextBox username = new() { Text = "Mustafa", Width = 120, MaxLength = 30, AccessibleName = "Kullanıcı adı" };
    private readonly TextBox draft = new() { Dock = DockStyle.Fill, MaxLength = 2000, AccessibleName = "Mesaj" };
    private readonly RichTextBox history = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
    private readonly RichTextBox serverHistory = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
    private readonly ListBox clients = new() { Dock = DockStyle.Fill };
    private readonly Button connect = new() { Text = "Bağlan", AutoSize = true };
    private readonly Button disconnect = new() { Text = "Bağlantıyı kes", AutoSize = true, Enabled = false };
    private readonly Button send = new() { Text = "Gönder", AutoSize = true, Enabled = false };
    private readonly Label status = new() { Text = "Bağlı değil", AutoSize = true, ForeColor = Color.DarkBlue };
    private bool closing;
    public Form1()
    {
        InitializeComponent(); Controls.Clear(); AutoScaleMode = AutoScaleMode.None;
        Text = "Fırat Chat | YMH219"; ClientSize = new(1000, 700); MinimumSize = new(850, 600); StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI", 10);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), RowCount = 3 };
        root.RowStyles.Add(new(SizeType.Absolute, 55)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 35));
        root.Controls.Add(new Label { Text = "FIRAT CHAT", AutoSize = true, Font = new("Segoe UI", 23, FontStyle.Bold), ForeColor = Color.FromArgb(37, 99, 235) });
        var tabs = new TabControl { Dock = DockStyle.Fill }; var clientTab = new TabPage("İstemci"); var serverTab = new TabPage("Sunucu"); tabs.TabPages.AddRange([clientTab, serverTab]);
        var clientLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(10), RowCount = 4 };
        clientLayout.RowStyles.Add(new(SizeType.Absolute, 90)); clientLayout.RowStyles.Add(new(SizeType.Absolute, 35)); clientLayout.RowStyles.Add(new(SizeType.Percent, 100)); clientLayout.RowStyles.Add(new(SizeType.Absolute, 45));
        var connection = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        connection.Controls.AddRange([new Label { Text = "Adres", AutoSize = true }, host, new Label { Text = "Port", AutoSize = true }, port, new Label { Text = "Ad", AutoSize = true }, username, connect, disconnect]);
        clientLayout.Controls.Add(connection, 0, 0); clientLayout.Controls.Add(status, 0, 1); clientLayout.Controls.Add(history, 0, 2);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new(0, 8, 0, 0) }; composer.ColumnStyles.Add(new(SizeType.Percent, 100)); composer.ColumnStyles.Add(new(SizeType.Absolute, 100)); composer.Controls.Add(draft); composer.Controls.Add(send); clientLayout.Controls.Add(composer, 0, 3); clientTab.Controls.Add(clientLayout);
        var serverLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(10), RowCount = 3 };
        serverLayout.RowStyles.Add(new(SizeType.Absolute, 80)); serverLayout.RowStyles.Add(new(SizeType.Percent, 65)); serverLayout.RowStyles.Add(new(SizeType.Percent, 35));
        var serverTools = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var serverPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 5055, Width = 85 };
        var lan = new CheckBox { Text = "Yerel ağ (LAN)", AutoSize = true };
        var start = new Button { Text = "Sunucuyu başlat", AutoSize = true }; var stop = new Button { Text = "Sunucuyu durdur", AutoSize = true, Enabled = false };
        serverTools.Controls.AddRange([new Label { Text = "Port", AutoSize = true }, serverPort, lan, start, stop]);
        serverLayout.Controls.Add(serverTools, 0, 0); serverLayout.Controls.Add(serverHistory, 0, 1); serverLayout.Controls.Add(clients, 0, 2); serverTab.Controls.Add(serverLayout);
        root.Controls.Add(tabs, 0, 1); root.Controls.Add(new Label { Text = "Mustafa Alkhatta · 250541607 | UTF-8 TCP sohbet", AutoSize = true }, 0, 2); Controls.Add(root);
        server.Message += line => Ui(() => serverHistory.AppendText(line + Environment.NewLine));
        server.ClientsChanged += list => Ui(() => { clients.Items.Clear(); clients.Items.AddRange(list); });
        client.Message += line => Ui(() => history.AppendText(line + Environment.NewLine));
        client.Disconnected += () => Ui(() => SetConnected(false));
        start.Click += (_, _) => { try { server.Start((int)serverPort.Value, lan.Checked); start.Enabled = serverPort.Enabled = lan.Enabled = false; stop.Enabled = true; } catch (Exception ex) { Error(ex); } };
        stop.Click += (_, _) => { server.Dispose(); clients.Items.Clear(); start.Enabled = serverPort.Enabled = lan.Enabled = true; stop.Enabled = false; serverHistory.AppendText("Sunucu durduruldu.\n"); };
        connect.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(username.Text) || string.IsNullOrWhiteSpace(host.Text)) { status.Text = "Adres ve kullanıcı adı gerekli."; return; }
            connect.Enabled = false; status.Text = "Bağlanıyor...";
            try { await client.Connect(host.Text.Trim(), (int)port.Value, username.Text.Trim()); SetConnected(true); }
            catch (Exception ex) { client.Dispose(); SetConnected(false); Error(ex); }
        };
        disconnect.Click += (_, _) => { client.Dispose(); SetConnected(false); };
        send.Click += async (_, _) => await Send();
        draft.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Send(); } };
    }
    private async Task Send()
    {
        if (!send.Enabled || string.IsNullOrWhiteSpace(draft.Text)) return;
        send.Enabled = false;
        try { await client.Send(draft.Text.Trim()); draft.Clear(); }
        catch (Exception ex) { client.Dispose(); SetConnected(false); Error(ex); }
        finally { send.Enabled = disconnect.Enabled; }
    }
    private void SetConnected(bool connected) { connect.Enabled = host.Enabled = port.Enabled = username.Enabled = !connected; disconnect.Enabled = send.Enabled = connected; status.Text = connected ? "BAĞLANDI · Mesaj gönderebilirsiniz" : "BAĞLI DEĞİL"; }
    private void Error(Exception ex) { status.Text = "İşlem başarısız: " + ex.Message; MessageBox.Show(this, ex.Message, "Bağlantı hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    private void Ui(Action action) { if (closing || IsDisposed || !IsHandleCreated) return; try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; client.Dispose(); server.Dispose(); base.OnFormClosing(e); }
}

