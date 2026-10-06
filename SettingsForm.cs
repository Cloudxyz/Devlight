using System.Diagnostics;

namespace Devlight;

internal sealed class SettingsForm : Form
{
    private readonly ComboBox _monitors = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _brightness = new() { Minimum = 0, Maximum = 100, Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(440, 0), Text = "Detecting monitors…" };
    private readonly Button _save = new() { Text = "Save", AutoSize = true, Enabled = false };
    private readonly AppSettings _settings;
    private readonly MonitorService _service;
    private readonly Func<AppSettings, bool> _persist;

    internal SettingsForm(AppSettings settings, MonitorService service, Func<AppSettings, bool> persist, Icon icon)
    {
        _settings = settings;
        _service = service;
        _persist = persist;
        Text = "Devlight — Settings";
        Icon = icon;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 245);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Monitor", AutoSize = true });
        layout.Controls.Add(_monitors);
        layout.Controls.Add(new Label { Text = "Target brightness (%)", AutoSize = true, Margin = new Padding(3, 12, 3, 3) });
        layout.Controls.Add(_brightness);
        layout.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        cancel.Click += (_, _) => Close();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_save);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = _save;
        CancelButton = cancel;
        _brightness.Value = Math.Clamp(settings.Brightness, 0, 100);
        _monitors.SelectedIndexChanged += (_, _) => UpdateSelection();
        _save.Click += (_, _) => Save();
        Shown += async (_, _) => await DiscoverAsync();
    }

    private async Task DiscoverAsync()
    {
        try
        {
            var monitors = await _service.DiscoverAsync();
            if (IsDisposed) return;
            _monitors.Items.AddRange(monitors.Cast<object>().ToArray());
            var selected = monitors.FirstOrDefault(m => MonitorIdentity.Matches(_settings.MonitorIdentity ?? "", m.Identity));
            if (selected is not null) _monitors.SelectedItem = selected;
            else if (!_settings.IsValid) _monitors.SelectedItem = monitors.FirstOrDefault(m => m.Supported);
            UpdateSelection();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (!IsDisposed) _status.Text = "Could not detect monitors. Reconnect the display and reopen Settings.";
        }
    }

    private void UpdateSelection()
    {
        var choice = _monitors.SelectedItem as MonitorChoice;
        _save.Enabled = choice?.Supported == true && !string.IsNullOrWhiteSpace(choice.Identity);
        _status.Text = choice is { Supported: true } ? "Save to apply brightness and restore it automatically."
            : choice?.Problem ?? (_monitors.Items.Count == 0 ? "No monitors found. Enable DDC/CI in your monitor's OSD and reopen Settings."
                : "Select a compatible monitor. The saved monitor may be disconnected.");
    }

    private void Save()
    {
        if (_monitors.SelectedItem is not MonitorChoice { Supported: true } choice || string.IsNullOrWhiteSpace(choice.Identity)) return;
        var settings = _settings with { MonitorIdentity = choice.Identity, MonitorName = choice.Name, Brightness = (int)_brightness.Value };
        if (_persist(settings)) Close();
    }
}
