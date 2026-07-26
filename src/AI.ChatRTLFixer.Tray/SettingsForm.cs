using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Win32;

namespace AI.ChatRTLFixer.Tray;

/// <summary>
/// Compact settings surface for the options people use most: turning the fix on,
/// choosing copy/font behavior, and enabling profiles. Advanced internals stay
/// out of the way so the tray app remains easy to understand.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly Orchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly SafeLogger _logger;
    private readonly UpdateChecker _updateChecker;
    private readonly Label _statusValue = new();
    private bool _loading;

    public SettingsForm(Orchestrator orchestrator, ISettingsStore settingsStore, SafeLogger logger, UpdateChecker updateChecker)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _logger = logger;
        _updateChecker = updateChecker;

        Text = "AI Chat RTL Fixer";
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(580, 760);
        MinimumSize = new Size(580, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);
        Build();
    }

    private void Build()
    {
        _loading = true;
        var settings = _orchestrator.Settings;
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(20),
            BackColor = BackColor,
        };
        Controls.Add(root);

        root.Controls.Add(BuildHeader());

        var general = MakeSection("General");
        var global = new CheckBox
        {
            Text = "Enable RTL Fixer",
            Checked = settings.GlobalEnabled,
            AutoSize = true,
            AccessibleName = "Enable RTL Fixer",
        };
        global.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            await _orchestrator.SetGlobalEnabledAsync(global.Checked);
            await SaveAsync();
            UpdateStatus();
        };
        general.Controls.Add(global);

        var startup = new CheckBox
        {
            Text = "Start with Windows",
            Checked = settings.StartWithWindows,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = "Start with Windows",
        };
        startup.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            settings.StartWithWindows = startup.Checked;
            try { StartupManager.SetEnabled(settings.StartWithWindows, Application.ExecutablePath); }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, LogCategories.App, "startup-set-failed", ("msg", SafeLogger.Redact(ex.Message)));
                MessageBox.Show("Windows startup could not be updated. Please try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            await SaveAsync();
        };
        general.Controls.Add(startup);

        var autoRelaunch = new CheckBox
        {
            Text = "Remember relaunch approval per app (skip asking again once you've said yes)",
            Checked = settings.AutoRelaunchAfterConsent,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = "Remember relaunch approval per app",
        };
        autoRelaunch.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            settings.AutoRelaunchAfterConsent = autoRelaunch.Checked;
            await SaveAsync();
        };
        general.Controls.Add(autoRelaunch);

        var browserTargets = new CheckBox
        {
            Text = "Enable browser targets (advanced; browser may be closed and reopened)",
            Checked = settings.EnableBrowserTargets,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = "Enable browser targets",
        };
        browserTargets.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            if (browserTargets.Checked && MessageBox.Show(
                    "Browser targeting can detect supported pages in your browser. A relaunch requires a separate confirmation and may close and reopen that browser. Continue?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                _loading = true;
                browserTargets.Checked = false;
                _loading = false;
                return;
            }

            await _orchestrator.SetBrowserTargetsEnabledAsync(browserTargets.Checked);
            await SaveAsync();
            UpdateStatus();
        };
        general.Controls.Add(browserTargets);

        var updateChecks = new CheckBox
        {
            Text = "Check GitHub for updates when the app starts",
            Checked = settings.CheckForUpdatesOnStartup,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = "Check for updates on startup",
        };
        updateChecks.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            settings.CheckForUpdatesOnStartup = updateChecks.Checked;
            await SaveAsync();
        };
        general.Controls.Add(updateChecks);

        LayoutSection(general);
        root.Controls.Add(general);

        var behavior = MakeSection("Chat behavior");
        var behaviorGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
        };
        behaviorGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        behaviorGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var fontLabel = new Label { Text = "Chat font", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 7) };
        var font = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 250,
            AccessibleName = "Chat font",
        };
        var availableFonts = Enum.GetValues<FontChoice>().Where(choice => choice != FontChoice.Custom).ToArray();
        font.Items.AddRange(availableFonts.Cast<object>().ToArray());
        font.SelectedItem = availableFonts.Contains(settings.SelectedFont) ? settings.SelectedFont : FontChoice.Vazirmatn;
        font.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading || font.SelectedItem is not FontChoice selected) return;
            settings.SelectedFont = selected;
            await _orchestrator.RefreshAttachedAsync();
            await SaveAsync();
        };

        var copyLabel = new Label { Text = "Copy mode", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 7) };
        var copy = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 250,
            AccessibleName = "Copy mode",
        };
        copy.Items.AddRange(Enum.GetValues<CopyMode>().Cast<object>().ToArray());
        copy.SelectedItem = settings.CopyMode;
        copy.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading || copy.SelectedItem is not CopyMode selected) return;
            settings.CopyMode = selected;
            await _orchestrator.RefreshAttachedAsync();
            await SaveAsync();
        };

        behaviorGrid.Controls.Add(fontLabel, 0, 0);
        behaviorGrid.Controls.Add(font, 1, 0);
        behaviorGrid.Controls.Add(copyLabel, 0, 1);
        behaviorGrid.Controls.Add(copy, 1, 1);
        behavior.Controls.Add(behaviorGrid);
        LayoutSection(behavior);
        root.Controls.Add(behavior);

        var profiles = MakeSection("App profiles");
        profiles.Controls.Add(new Label
        {
            Text = "Only enable profiles you recognize. Experimental profiles can change when the target app updates.",
            AutoSize = false,
            Width = 480,
            Height = 34,
            ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(0, 0, 0, 8),
        });
        var profileList = new CheckedListBox
        {
            CheckOnClick = true,
            Width = 480,
            Height = 190,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Enabled app profiles",
        };
        foreach (var profile in _orchestrator.Profiles.OrderBy(profile => profile.DisplayName))
        {
            // Opt-in: a profile with no saved toggle yet has never been enabled by
            // the user, so the checkbox must start unchecked, not pre-ticked.
            var enabled = settings.Apps.TryGetValue(profile.AppId, out var toggle) && toggle.Enabled;
            profileList.Items.Add(new ProfileChoice(profile), enabled);
        }
        // Size the list to show every profile so the last apps (OpenCode, ZCode)
        // are visible without scrolling — otherwise they look unsupported.
        profileList.Height = Math.Clamp(profileList.Items.Count * profileList.ItemHeight + 6, 190, 360);
        profileList.ItemCheck += (_, args) => BeginInvoke(async () =>
        {
            if (_loading || profileList.Items[args.Index] is not ProfileChoice choice) return;
            var enabled = profileList.GetItemChecked(args.Index);
            await _orchestrator.SetAppEnabledAsync(choice.Profile.AppId, enabled);
            await SaveAsync();
            UpdateStatus();
        });
        profiles.Controls.Add(profileList);
        LayoutSection(profiles);
        root.Controls.Add(profiles);

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Width = 520,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 4, 0, 0),
        };
        var restore = new Button { Text = "Restore and pause", AutoSize = true, AccessibleName = "Restore current changes and pause" };
        restore.Click += async (_, _) =>
        {
            await _orchestrator.SetGlobalEnabledAsync(false);
            _loading = true;
            global.Checked = false;
            _loading = false;
            await SaveAsync();
            UpdateStatus();
        };
        var checkUpdates = new Button { Text = "Check for updates", AutoSize = true, AccessibleName = "Check for updates" };
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, AccessibleName = "Close settings" };
        footer.Controls.Add(restore);
        footer.Controls.Add(checkUpdates);
        footer.Controls.Add(close);
        root.Controls.Add(footer);

        _loading = false;
        UpdateStatus();
    }

    private Control BuildHeader()
    {
        var header = new Panel { Width = 520, Height = 76, Margin = new Padding(0, 0, 0, 12) };
        header.Controls.Add(new Label
        {
            Text = "AI Chat RTL Fixer",
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0),
        });
        header.Controls.Add(new Label
        {
            Text = "RTL only where it belongs: chat text stays readable, code stays LTR.",
            AutoSize = true,
            ForeColor = Color.FromArgb(71, 85, 105),
            Location = new Point(0, 28),
        });
        _statusValue.AutoSize = true;
        _statusValue.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        _statusValue.Location = new Point(0, 53);
        header.Controls.Add(_statusValue);
        return header;
    }

    private static GroupBox MakeSection(string title) => new()
    {
        Text = title,
        Width = 520,
        AutoSize = false,
        Padding = new Padding(14, 24, 14, 14),
        Margin = new Padding(0, 0, 0, 12),
    };

    private static void LayoutSection(GroupBox section)
    {
        var y = 28;
        foreach (Control control in section.Controls)
        {
            y += control.Margin.Top;
            control.Location = new Point(14, y);
            y += control.Height + 8;
        }
        section.Height = y + 12;
    }

    private async Task SaveAsync() => await _settingsStore.SaveAsync(_orchestrator.Settings, CancellationToken.None);

    private async Task CheckForUpdatesAsync()
    {
        var result = await _updateChecker.CheckAsync(CancellationToken.None);
        if (result.IsUpdateAvailable)
        {
            var open = MessageBox.Show(
                $"Version {result.LatestVersion} is available. Open the GitHub release page?",
                Constants.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes && result.ReleasePage is not null)
                UpdateChecker.OpenReleasePage(result.ReleasePage);
            return;
        }

        MessageBox.Show(result.Message, Constants.ProductName,
            MessageBoxButtons.OK, result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void UpdateStatus()
    {
        var attached = _orchestrator.RuntimeStatuses.Count(status => status.State == AppRuntimeState.InjectionSucceeded);
        _statusValue.Text = _orchestrator.Settings.GlobalEnabled
            ? attached > 0 ? $"Status: active in {attached} app{(attached == 1 ? string.Empty : "s")}" : "Status: enabled — waiting for a supported app"
            : "Status: paused — no app is being modified";
        _statusValue.ForeColor = _orchestrator.Settings.GlobalEnabled ? Color.FromArgb(13, 148, 136) : Color.FromArgb(100, 116, 139);
    }

    private sealed record ProfileChoice(AppProfile Profile)
    {
        public override string ToString() => $"{Profile.DisplayName} — {Profile.Status}";
    }
}
