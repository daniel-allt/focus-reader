using FocusReader.Native;

namespace FocusReader.Forms;

internal sealed class SettingsForm : Form
{
    private readonly Func<AppSettings, string?> _apply;
    private readonly Action<bool> _setHotkeySuspended;

    private readonly ComboBox _cursorCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Panel _cursorPreview = new() { Size = new Size(36, 36), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
    private readonly TextBox _customCursorPath = new() { Width = 260 };
    private readonly Button _browseCursor = new() { Text = "Procurar...", AutoSize = true };
    private readonly Panel _colorSwatch = new() { Size = new Size(48, 24), BorderStyle = BorderStyle.FixedSingle };
    private readonly Button _pickColor = new() { Text = "Escolher cor...", AutoSize = true };
    private readonly TrackBar _opacity = new()
    {
        Minimum = AppSettings.MinOpacity,
        Maximum = AppSettings.MaxOpacity,
        TickFrequency = 10,
        SmallChange = 1,
        LargeChange = 5,
        Width = 220,
    };
    private readonly Label _opacityLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown _width = new() { Minimum = 40, Maximum = 4000, Increment = 10, Width = 70 };
    private readonly NumericUpDown _height = new() { Minimum = 6, Maximum = 600, Width = 70 };
    private readonly NumericUpDown _offset = new() { Minimum = -200, Maximum = 200, Width = 70 };
    private readonly CheckBox _stabilize = new() { Text = "Ativada", AutoSize = true };
    private readonly NumericUpDown _tolerance = new() { Minimum = 1, Maximum = 400, Width = 70 };
    private readonly NumericUpDown _followMs = new() { Minimum = 100, Maximum = 5000, Increment = 100, Width = 70 };
    private readonly HotkeyTextBox _hotkeyBox = new() { Width = 220 };
    private readonly Button _clearHotkey = new() { Text = "Limpar", AutoSize = true };
    private readonly PreviewPanel _preview = new() { Size = new Size(480, 110), Margin = new Padding(3, 10, 3, 3) };
    private readonly Button _save = new() { Text = "Salvar", AutoSize = true, MinimumSize = new Size(90, 0) };
    private readonly Button _cancel = new() { Text = "Cancelar", AutoSize = true, MinimumSize = new Size(90, 0) };
    private readonly Button _defaults = new() { Text = "Restaurar padrões", AutoSize = true };

    private bool _loading;

    public SettingsForm(AppSettings current, Func<AppSettings, string?> apply, Action<bool> setHotkeySuspended, Icon icon)
    {
        _apply = apply;
        _setHotkeySuspended = setHotkeySuspended;

        Text = "Focus Reader — Configurações";
        Icon = icon;
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        AcceptButton = _save;
        CancelButton = _cancel;

        foreach (var option in CursorChoices.All)
            _cursorCombo.Items.Add(option);

        BuildLayout();
        WireEvents();
        LoadValues(current);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            if (_hotkeyBox.ContainsFocus)
                ActiveControl = _cursorCombo;

            _setHotkeySuspended(false);
            Hide();
            WindowState = FormWindowState.Normal;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _setHotkeySuspended(false);
        base.OnFormClosed(e);
    }

    private void BuildLayout()
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(Padding.Left, Padding.Top),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(table, "Cursor ao ativar:", Flow(_cursorCombo, _cursorPreview));
        AddRow(table, "Arquivo do cursor:", Flow(_customCursorPath, _browseCursor));
        AddRow(table, "Cor do realce:", Flow(_colorSwatch, _pickColor));
        AddRow(table, "Opacidade:", Flow(_opacity, _opacityLabel));
        AddRow(table, "Tamanho do realce:", Flow(_width, Caption("x"), _height, Caption("px (largura x altura)")));
        AddRow(table, "Distância do cursor:", Flow(_offset, Caption("px acima da ponta do cursor")));
        AddRow(table, "Estabilização vertical:", Flow(_stabilize, _tolerance, Caption("px de tolerância antes de mudar de linha")));
        AddRow(table, "Ajuste da linha:", Flow(_followMs, Caption("ms seguindo o cursor depois de destravar, antes de travar de novo")));
        AddRow(table, "Atalho global:", Flow(_hotkeyBox, _clearHotkey));

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Clique no campo do atalho e pressione a combinação desejada (ex.: Ctrl + Alt + R).",
            Margin = new Padding(3, 0, 3, 6),
        };
        table.Controls.Add(hint);
        table.SetColumnSpan(hint, 2);

        table.Controls.Add(_preview);
        table.SetColumnSpan(_preview, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.AddRange([_cancel, _save, _defaults]);
        table.Controls.Add(buttons);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 6) });
        table.Controls.Add(control);
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Left };
        foreach (var c in controls)
        {
            if (c is not Label)
                c.Anchor = AnchorStyles.Left;
            panel.Controls.Add(c);
        }

        return panel;
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 0) };

    private void WireEvents()
    {
        _cursorCombo.SelectedIndexChanged += (_, _) => OnCursorChanged();
        _customCursorPath.TextChanged += (_, _) => _cursorPreview.Invalidate();
        _browseCursor.Click += (_, _) => BrowseCursor();
        _cursorPreview.Paint += PaintCursorPreview;
        _pickColor.Click += (_, _) => PickColor();
        _colorSwatch.Click += (_, _) => PickColor();
        _opacity.ValueChanged += (_, _) => UpdatePreview();
        _width.ValueChanged += (_, _) => UpdatePreview();
        _height.ValueChanged += (_, _) => UpdatePreview();
        _stabilize.CheckedChanged += (_, _) => _tolerance.Enabled = _followMs.Enabled = _stabilize.Checked;
        _clearHotkey.Click += (_, _) => _hotkeyBox.Hotkey = null;
        _hotkeyBox.Enter += (_, _) => _setHotkeySuspended(true);
        _hotkeyBox.Leave += (_, _) => _setHotkeySuspended(false);
        _save.Click += (_, _) => Save();
        _cancel.Click += (_, _) => Close();
        _defaults.Click += (_, _) => LoadValues(new AppSettings());
    }

    private void LoadValues(AppSettings s)
    {
        _loading = true;
        _cursorCombo.SelectedItem = CursorChoices.All.FirstOrDefault(o => o.Value == s.Cursor) ?? CursorChoices.All[0];
        _customCursorPath.Text = s.CustomCursorPath ?? string.Empty;
        _colorSwatch.BackColor = s.OverlayColor;
        _opacity.Value = s.OverlayOpacity;
        _width.Value = s.OverlayWidth;
        _height.Value = s.OverlayHeight;
        _offset.Value = s.CursorOffset;
        _stabilize.Checked = s.VerticalStabilization;
        _tolerance.Value = s.VerticalTolerance;
        _followMs.Value = s.VerticalFollowMs;
        _tolerance.Enabled = _followMs.Enabled = s.VerticalStabilization;
        _hotkeyBox.Hotkey = s.Hotkey;
        _loading = false;
        OnCursorChanged();
        UpdatePreview();
    }

    private AppSettings ReadValues()
    {
        var s = new AppSettings
        {
            Cursor = SelectedCursor,
            CustomCursorPath = string.IsNullOrWhiteSpace(_customCursorPath.Text) ? null : _customCursorPath.Text.Trim(),
            OverlayColor = _colorSwatch.BackColor,
            OverlayOpacity = _opacity.Value,
            OverlayWidth = (int)_width.Value,
            OverlayHeight = (int)_height.Value,
            CursorOffset = (int)_offset.Value,
            VerticalStabilization = _stabilize.Checked,
            VerticalTolerance = (int)_tolerance.Value,
            VerticalFollowMs = (int)_followMs.Value,
            Hotkey = _hotkeyBox.Hotkey,
        };
        s.Normalize();
        return s;
    }

    private CursorChoice SelectedCursor =>
        (_cursorCombo.SelectedItem as CursorOption)?.Value ?? CursorChoice.Hand;

    private void OnCursorChanged()
    {
        var custom = SelectedCursor == CursorChoice.Custom;
        _customCursorPath.Enabled = custom;
        _browseCursor.Enabled = custom;
        _cursorPreview.Invalidate();
    }

    private void UpdatePreview()
    {
        if (_loading)
            return;

        _opacityLabel.Text = $"{_opacity.Value}% opaco ({100 - _opacity.Value}% transparente)";
        _preview.HighlightColor = _colorSwatch.BackColor;
        _preview.HighlightOpacity = _opacity.Value;
        _preview.HighlightWidth = (int)_width.Value;
        _preview.HighlightHeight = (int)_height.Value;
        _preview.Invalidate();
    }

    private void BrowseCursor()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Selecionar cursor",
            Filter = "Cursores (*.cur;*.ani)|*.cur;*.ani|Todos os arquivos (*.*)|*.*",
            InitialDirectory = Directory.Exists(@"C:\Windows\Cursors") ? @"C:\Windows\Cursors" : string.Empty,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _customCursorPath.Text = dialog.FileName;
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = _colorSwatch.BackColor, FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _colorSwatch.BackColor = dialog.Color;
        UpdatePreview();
    }

    private void PaintCursorPreview(object? sender, PaintEventArgs e)
    {
        var bounds = new Rectangle(2, 2, 32, 32);
        var choice = SelectedCursor;
        if (choice == CursorChoice.Custom)
        {
            var path = _customCursorPath.Text.Trim();
            if (!File.Exists(path))
                return;

            var handle = NativeMethods.LoadCursorFromFile(path);
            if (handle == IntPtr.Zero)
                return;

            try
            {
                new Cursor(handle).Draw(e.Graphics, bounds);
            }
            finally
            {
                NativeMethods.DestroyCursor(handle);
            }

            return;
        }

        CursorChoices.WinFormsCursor(choice)?.Draw(e.Graphics, bounds);
    }

    private void Save()
    {
        var settings = ReadValues();
        if (settings.Cursor == CursorChoice.Custom &&
            (settings.CustomCursorPath is null || !File.Exists(settings.CustomCursorPath)))
        {
            MessageBox.Show(this, "Selecione um arquivo de cursor (.cur ou .ani) válido.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var error = _apply(settings);
        if (error is not null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Close();
    }
}
