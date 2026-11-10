using System.Diagnostics;

namespace FocusReader.Forms;

internal sealed class AboutForm : Form
{
    private const string RepositoryUrl = "https://github.com/daniel-allt/focus-reader";

    public AboutForm()
    {
        Text = "Sobre o Focus Reader";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        using (var stream = typeof(AboutForm).Assembly.GetManifestResourceStream("FocusReader.app.ico"))
        {
            if (stream is not null)
                Icon = new Icon(stream);
        }

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Location = new Point(Padding.Left, Padding.Top),
        };

        var logo = new PictureBox
        {
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon is null ? null : new Icon(Icon, 48, 48).ToBitmap(),
            Margin = new Padding(0, 0, 16, 0),
        };
        layout.Controls.Add(logo, 0, 0);
        layout.SetRowSpan(logo, 5);

        var details = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
        };
        details.Controls.Add(new Label
        {
            Text = "Focus Reader",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        });
        details.Controls.Add(Line($"Versão {AppVersion.Display}"));
        if (AppVersion.Commit is { } commit)
            details.Controls.Add(Line($"Commit {commit}"));
        details.Controls.Add(Line("Realce de leitura que acompanha o cursor.", top: 10));

        var link = new LinkLabel { Text = RepositoryUrl, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        details.Controls.Add(link);

        var close = new Button { Text = "Fechar", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 16, 0, 0) };
        close.Click += (_, _) => Close();
        details.Controls.Add(close);
        details.SetFlowBreak(close, false);

        layout.Controls.Add(details, 1, 0);
        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }

    private static Label Line(string text, int top = 2) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, top, 0, 0) };
}
