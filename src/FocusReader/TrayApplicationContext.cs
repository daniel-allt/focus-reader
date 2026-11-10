using FocusReader.Forms;
using Microsoft.Win32;

namespace FocusReader;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly OverlayForm _overlay = new();
    private readonly GlobalHotkey _hotkey = new();
    private readonly Icon _idleIcon = LoadIcon("app.ico");
    private readonly Icon _activeIcon = LoadIcon("app-active.ico");

    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private AboutForm? _aboutForm;
    private bool _active;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();

        _toggleItem = new ToolStripMenuItem("Ativar modo de leitura", null, (_, _) => Toggle());
        var settingsItem = new ToolStripMenuItem("Configurações...", null, (_, _) => OpenSettings())
        {
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
        };
        var aboutItem = new ToolStripMenuItem("Sobre...", null, (_, _) => OpenAbout());
        var exitItem = new ToolStripMenuItem("Sair", null, (_, _) => ExitThread());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_toggleItem, settingsItem, aboutItem, new ToolStripSeparator(), exitItem]);

        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _hotkey.Pressed += (_, _) => Toggle();
        SystemEvents.SessionEnding += OnSessionEnding;

        var registered = _hotkey.Register(_settings.Hotkey);
        UpdateUi();

        if (registered)
        {
            var how = _settings.Hotkey is null
                ? "Use o menu do ícone para ativar o modo de leitura."
                : $"Pressione {_settings.Hotkey} para ativar/desativar o modo de leitura.";
            _tray.ShowBalloonTip(4000, "Focus Reader", $"Executando na bandeja do sistema. {how}", ToolTipIcon.Info);
        }
        else
        {
            ShowHotkeyError(_settings.Hotkey);
        }
    }

    private void Toggle() => SetActive(!_active);

    private void OpenAbout()
    {
        if (_aboutForm is { IsDisposed: false })
        {
            _aboutForm.Activate();
            return;
        }

        _aboutForm = new AboutForm();
        _aboutForm.FormClosed += (_, _) => _aboutForm = null;
        _aboutForm.Show();
    }

    private void SetActive(bool active)
    {
        if (active)
        {
            try
            {
                SystemCursorManager.Apply(_settings.Cursor, _settings.CustomCursorPath);
            }
            catch (Exception ex)
            {
                SystemCursorManager.Restore();
                _overlay.StopFollowing();
                _active = false;
                UpdateUi();
                _tray.ShowBalloonTip(6000, "Focus Reader",
                    $"{ex.Message} O modo de leitura não foi ativado. Verifique o cursor nas Configurações.",
                    ToolTipIcon.Warning);
                return;
            }

            _overlay.ApplySettings(_settings);
            _overlay.StartFollowing();
        }
        else
        {
            _overlay.StopFollowing();
            SystemCursorManager.Restore();
        }

        _active = active;
        UpdateUi();
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Show();
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, TryApplySettings, SetHotkeySuspended, _idleIcon);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private string? TryApplySettings(AppSettings newSettings)
    {
        if (!SystemCursorManager.CanLoad(newSettings.Cursor, newSettings.CustomCursorPath))
            return "Não foi possível carregar o cursor selecionado. Escolha outro cursor ou outro arquivo .cur/.ani.";

        if (!_hotkey.Register(newSettings.Hotkey))
        {
            _hotkey.Register(_settings.Hotkey);
            return $"Não foi possível registrar o atalho {newSettings.Hotkey}. " +
                   "Ele pode já estar em uso pelo Windows ou por outro programa. Escolha outra combinação.";
        }

        _settings = newSettings;
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            return $"As configurações foram aplicadas, mas não puderam ser salvas: {ex.Message}";
        }

        if (_active)
            SetActive(true);

        UpdateUi();
        return null;
    }

    private void SetHotkeySuspended(bool suspended)
    {
        if (suspended)
            _hotkey.Unregister();
        else
            _hotkey.Register(_settings.Hotkey);
    }

    private void UpdateUi()
    {
        _toggleItem.Text = _active ? "Desativar modo de leitura" : "Ativar modo de leitura";
        _toggleItem.Checked = _active;
        _toggleItem.ShortcutKeyDisplayString = _settings.Hotkey?.ToString() ?? string.Empty;
        _tray.Icon = _active ? _activeIcon : _idleIcon;
        _tray.Text = _active ? "Focus Reader — modo de leitura ativo" : "Focus Reader — inativo";
    }

    private void ShowHotkeyError(HotkeyDefinition? hotkey) =>
        _tray.ShowBalloonTip(6000, "Focus Reader",
            $"Não foi possível registrar o atalho {hotkey}. Ele pode estar em uso por outro programa. " +
            "Abra as Configurações para escolher outro.", ToolTipIcon.Warning);

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => SystemCursorManager.Restore();

    protected override void ExitThreadCore()
    {
        SetActive(false);
        SystemEvents.SessionEnding -= OnSessionEnding;
        _settingsForm?.Close();
        _aboutForm?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _hotkey.Dispose();
        _overlay.Dispose();
        base.ExitThreadCore();
    }

    private static Icon LoadIcon(string name)
    {
        using var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream($"FocusReader.{name}")
                           ?? throw new InvalidOperationException($"Recurso {name} não encontrado.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
