namespace ChatApp;

internal sealed class Conversation : Panel
{
    private const int VisibleLimit = 150;
    private readonly TableLayoutPanel timeline = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty, Padding = new(12, 10, 12, 8) };
    private readonly List<Bubble> bubbles = [];

    public Conversation()
    {
        AutoScroll = true;
        BackColor = Color.FromArgb(229, 242, 240);
        Dock = DockStyle.Fill;
        Margin = new(0, 7, 0, 6);
        timeline.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(timeline);
        SizeChanged += (_, _) => ResizeMessages();
    }

    public void Add(RoomMessage message, Guid own)
    {
        bool nearBottom = VerticalScroll.Value + ClientSize.Height + 55 >= timeline.Height;
        if (bubbles.Count == VisibleLimit)
        {
            var first = bubbles[0];
            timeline.Controls.Remove(first);
            bubbles.RemoveAt(0);
            first.Dispose();
        }
        var bubble = new Bubble(message, message.Sender != Guid.Empty && message.Sender == own);
        bubbles.Add(bubble);
        ResizeMessage(bubble);
        // With a single column the control collection is also the timeline order.
        timeline.Controls.Add(bubble);
        if (nearBottom) ScrollControlIntoView(bubble);
    }

    public void Clear()
    {
        foreach (var bubble in bubbles) bubble.Dispose();
        bubbles.Clear();
        AutoScrollPosition = Point.Empty;
    }

    private void ResizeMessages()
    {
        timeline.SuspendLayout();
        foreach (var bubble in bubbles) ResizeMessage(bubble);
        timeline.ResumeLayout();
    }

    private void ResizeMessage(Bubble bubble)
    {
        int available = ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 35;
        bubble.Wrap(Math.Clamp(available * 7 / 10, 140, 660));
    }

    private sealed class Bubble : Panel
    {
        private readonly Label body;
        private readonly Label header;

        public Bubble(RoomMessage message, bool mine)
        {
            bool notice = message.Kind != MessageKind.Chat;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Anchor = notice ? AnchorStyles.None : mine ? AnchorStyles.Right : AnchorStyles.Left;
            Padding = notice ? new(8, 4, 8, 4) : new(13, 8, 13, 9);
            Margin = new(0, 3, 0, 6);
            BackColor = notice ? Color.FromArgb(213, 231, 228) : mine ? Ui.Tint : Color.White;
            header = Ui.Label(
                notice ? $"{message.Time.ToLocalTime():HH:mm}" : $"{(mine ? "Siz · " : "")}{message.Name}    {message.Time.ToLocalTime():HH:mm}",
                9.5f, true, Ui.Accent);
            body = Ui.Label(message.Text, notice ? 9.5f : 11f, ink: notice ? Ui.Muted : Ui.Text);
            var content = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
            content.Controls.Add(header, 0, 0);
            content.Controls.Add(body, 0, 1);
            Controls.Add(content);
            AccessibleName = notice ? "Oda bildirimi" : "Mesaj";
        }

        public void Wrap(int width) => header.MaximumSize = body.MaximumSize = new(width, 0);
    }
}
