namespace FocusReader.Forms;

/// <summary>Shows sample text with the highlight drawn over the middle line.</summary>
internal sealed class PreviewPanel : Panel
{
    private static readonly string[] SampleLines =
    [
        "A leitura com foco ajuda a manter a atenção",
        "somente nas palavras que estão sob o realce,",
        "reduzindo as distrações do restante da página.",
    ];

    public Color HighlightColor { get; set; } = Color.Yellow;
    public int HighlightOpacity { get; set; } = 35;
    public int HighlightWidth { get; set; } = 85;
    public int HighlightHeight { get; set; } = 30;

    public PreviewPanel()
    {
        DoubleBuffered = true;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var font = new Font("Segoe UI", 11f);
        var lineHeight = (int)(font.GetHeight(g) * 1.35f);
        var top = (ClientSize.Height - lineHeight * SampleLines.Length) / 2;

        for (var i = 0; i < SampleLines.Length; i++)
            TextRenderer.DrawText(g, SampleLines[i], font, new Point(12, top + i * lineHeight), Color.FromArgb(40, 40, 40));

        var scale = DeviceDpi / 96f;
        var width = Math.Min((int)(HighlightWidth * scale), ClientSize.Width);
        var height = (int)(HighlightHeight * scale);
        var centerY = top + lineHeight + lineHeight / 2;
        var rect = new Rectangle((ClientSize.Width - width) / 2, centerY - height / 2, width, height);

        var alpha = (int)Math.Round(HighlightOpacity * 255 / 100.0);
        using var brush = new SolidBrush(Color.FromArgb(alpha, HighlightColor));
        g.FillRectangle(brush, rect);
    }
}
