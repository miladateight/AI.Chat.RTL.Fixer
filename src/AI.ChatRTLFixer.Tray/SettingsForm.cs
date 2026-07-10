using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Win32;

namespace AI.ChatRTLFixer.Tray;

/// <summary>Settings form: global/per-app toggles, font, copy mode, startup, security note.</summary>
public sealed class SettingsForm : Form
{
    private readonly Orchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly SafeLogger _logger;

    public SettingsForm(Orchestrator orchestrator, ISettingsStore settingsStore, SafeLogger logger)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _logger = logger;
        Text = "AI Chat RTL Fixer — Settings";
        Width = 480; Height = 520;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Build();
    }

    private void Build()
    {
        var s = _orchestrator.Settings;
        var y = 12;

        var chkGlobal = new CheckBox { Text = "Enable AI Chat RTL Fixer (global)", Checked = s.GlobalEnabled, Top = y, Left = 16, Width = 320 };
        chkGlobal.CheckedChanged += async (_, _) =>
        {
            s.GlobalEnabled = chkGlobal.Checked;
            if (!s.GlobalEnabled) await _orchestrator.DisableAllAsync();
            await _settingsStore.SaveAsync(s, CancellationToken.None);
        };
        Controls.Add(chkGlobal); y += 32;

        // Per-app toggles
        var lblApps = new Label { Text = "Detected / supported apps:", Top = y, Left = 16, Width = 320 };
        Controls.Add(lblApps); y += 22;
        foreach (var p in _orchestrator.Profiles)
        {
            var enabled = s.Apps.TryGetValue(p.AppId, out var st) ? st.Enabled : false;
            var chk = new CheckBox { Text = $"{p.DisplayName} — {p.Status}", Checked = enabled, Top = y, Left = 32, Width = 380, Tag = p.AppId };
            chk.CheckedChanged += async (_, _) =>
            {
                s.Apps[p.AppId] = new AppToggleState { Enabled = chk.Checked };
                await _settingsStore.SaveAsync(s, CancellationToken.None);
            };
            Controls.Add(chk); y += 24;
        }

        y += 6;
        var lblFont = new Label { Text = "Font:", Top = y, Left = 16, Width = 60 }; Controls.Add(lblFont);
        var comboFont = new ComboBox { Top = y, Left = 80, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        comboFont.Items.AddRange(Enum.GetNames<FontChoice>());
        comboFont.SelectedItem = s.SelectedFont.ToString();
        comboFont.SelectedIndexChanged += async (_, _) =>
        {
            s.SelectedFont = Enum.Parse<FontChoice>(comboFont.SelectedItem!.ToString()!);
            await _settingsStore.SaveAsync(s, CancellationToken.None);
        };
        Controls.Add(comboFont); y += 28;

        var lblCopy = new Label { Text = "Copy mode:", Top = y, Left = 16, Width = 80 }; Controls.Add(lblCopy);
        var comboCopy = new ComboBox { Top = y, Left = 100, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        comboCopy.Items.AddRange(Enum.GetNames<CopyMode>());
        comboCopy.SelectedItem = s.CopyMode.ToString();
        comboCopy.SelectedIndexChanged += async (_, _) =>
        {
            s.CopyMode = Enum.Parse<CopyMode>(comboCopy.SelectedItem!.ToString()!);
            await _settingsStore.SaveAsync(s, CancellationToken.None);
        };
        Controls.Add(comboCopy); y += 28;

        var chkStartup = new CheckBox { Text = "Start with Windows", Checked = s.StartWithWindows, Top = y, Left = 16, Width = 200 };
        chkStartup.CheckedChanged += async (_, _) =>
        {
            s.StartWithWindows = chkStartup.Checked;
            try { StartupManager.SetEnabled(s.StartWithWindows, Application.ExecutablePath); }
            catch (Exception ex) { _logger.Log(LogLevel.Warning, LogCategories.App, "startup-set-failed", ("msg", SafeLogger.Redact(ex.Message))); }
            await _settingsStore.SaveAsync(s, CancellationToken.None);
        };
        Controls.Add(chkStartup); y += 28;

        var chkDev = new CheckBox { Text = "Developer mode (allow short text samples in logs)", Checked = s.DeveloperMode, Top = y, Left = 16, Width = 380 };
        chkDev.CheckedChanged += async (_, _) =>
        {
            if (chkDev.Checked && MessageBox.Show(
                "Developer mode allows SHORT (truncated) text samples from the chat " +
                "surface to appear in local logs. This is only for debugging. Enable?",
                "Developer mode warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            { chkDev.Checked = false; return; }
            s.DeveloperMode = chkDev.Checked;
            await _settingsStore.SaveAsync(s, CancellationToken.None);
        };
        Controls.Add(chkDev); y += 32;

        var secNote = new Label
        {
            Text = "Security: This tool uses Chrome DevTools Protocol over local " +
                   "loopback (127.0.0.1) only. No external network calls are made.",
            Top = y, Left = 16, Width = 440, Height = 48,
        };
        Controls.Add(secNote); y += 50;

        var privacyNote = new Label
        {
            Text = "Privacy: No telemetry. No analytics. No chat content stored or sent anywhere.",
            Top = y, Left = 16, Width = 440, Height = 32,
        };
        Controls.Add(privacyNote);
    }
}