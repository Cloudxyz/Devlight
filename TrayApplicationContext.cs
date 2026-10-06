using System.Diagnostics;

namespace Devlight;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _store = new();
    private readonly StartupRegistration _startup = new();
    private readonly MonitorService _monitors = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _apply = new("Apply brightness");
    private readonly ToolStripMenuItem _start = new("Start with Windows");
    private readonly Icon _icon;
    private readonly BrightnessRestorer _restorer;
    private readonly DisplayEventWindow _displayEvents;
    private AppSettings _settings;
    private SettingsForm? _form;
    private bool _applying;
    private bool _exiting;

    internal TrayApplicationContext()
    {
        // Tray-only startup must also marshal async UI continuations to the main thread.
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _settings = _store.Load();
        _restorer = new BrightnessRestorer(_monitors.ApplyAsync);
        using var resource = typeof(Program).Assembly.GetManifestResourceStream("Devlight.Assets.DevLight.ico")!;
        _icon = new Icon(resource);
        _tray = new NotifyIcon { Icon = _icon, Text = "Devlight", ContextMenuStrip = _menu, Visible = true };
        _menu.Items.Add(_apply);
        _menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        _menu.Items.Add(_start);
        _menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _start.Checked = _settings.StartWithWindows;
        _start.Click += (_, _) => Persist(_settings with { StartWithWindows = !_settings.StartWithWindows });
        _apply.Click += async (_, _) => await ApplyAsync();
        _tray.MouseClick += async (_, e) => { if (e.Button == MouseButtons.Left) await ApplyAsync(); };
        _displayEvents = new DisplayEventWindow();
        _displayEvents.AvailabilityChanged += RestoreBrightness;
        // Defer first-run UI until the Windows message loop is running.
        Application.Idle += FirstIdle;
    }

    private void FirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= FirstIdle;
        if (!_settings.IsValid) ShowSettings();
        else RestoreBrightness();
    }

    private void RestoreBrightness()
    {
        if (!_exiting) _restorer.Restore(_settings);
    }

    private void ShowSettings()
    {
        if (_exiting) return;
        if (_form is { IsDisposed: false }) { _form.Activate(); return; }
        _form = new SettingsForm(_settings, _monitors,
            settings => Persist(settings with { StartWithWindows = _settings.StartWithWindows }), _icon);
        _form.Show();
    }

    private bool Persist(AppSettings settings)
    {
        string? previousCommand = null;
        bool registryChanged = false;
        try
        {
            previousCommand = _startup.Read();
            _startup.Set(settings.StartWithWindows);
            registryChanged = true;
            _store.Save(settings);
            bool targetChanged = !MonitorIdentity.Matches(_settings.MonitorIdentity ?? "", settings.MonitorIdentity ?? "")
                || _settings.Brightness != settings.Brightness;
            _settings = settings;
            _start.Checked = settings.StartWithWindows;
            if (targetChanged) RestoreBrightness();
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (registryChanged)
            {
                try { _startup.Restore(previousCommand); }
                catch (Exception rollback) { Debug.WriteLine(rollback); Error("Settings could not be saved and Windows startup could not be restored. Check the startup entry and try again."); return false; }
            }
            Error("Could not save settings or update Windows startup. Check your user profile permissions and try again.");
            return false;
        }
    }

    private async Task ApplyAsync()
    {
        if (_applying || _exiting) return;
        if (!_settings.IsValid) { ShowSettings(); return; }
        _applying = true;
        _apply.Enabled = false;
        try { await _restorer.ApplyManualAsync(_settings); }
        catch (OperationCanceledException) { /* Superseded by Settings or application exit. */ }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (!_exiting) Error(exception is InvalidOperationException ? exception.Message : "Brightness could not be changed. Reconnect the monitor and check DDC/CI.");
        }
        finally { _applying = false; if (!_exiting) _apply.Enabled = true; }
    }

    private void Error(string message) => _tray.ShowBalloonTip(6000, "Devlight", message, ToolTipIcon.Error);

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _menu.Enabled = false;
        _displayEvents.AvailabilityChanged -= RestoreBrightness;
        _displayEvents.Dispose();
        _form?.Close();
        await _restorer.StopAsync();
        await _monitors.StopAsync(); // Wait for scoped native handles to be released before exiting.
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.Idle -= FirstIdle;
            _exiting = true;
            _displayEvents.AvailabilityChanged -= RestoreBrightness;
            _displayEvents.Dispose();
            // Also drain when the Windows message loop terminates externally.
            _restorer.StopAsync().GetAwaiter().GetResult();
            _monitors.StopAsync().GetAwaiter().GetResult();
            _form?.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
