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
        ClientSize = new Size(580, 690);
        MinimumSize = new Size(580, 520);
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

        var general = MakeSection("Quick setup");
        var global = new CheckBox
        {
            Text = "Turn on RTL Fixer",
            Checked = settings.GlobalEnabled,
            AutoSize = true,
            AccessibleName = "Turn on RTL Fixer",
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
            Text = "Start automatically with Windows",
            Checked = settings.StartWithWindows,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = "Start with Windows",
        };
        startup.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            var previous = settings.StartWithWindows;
            var requested = startup.Checked;
            try
            {
                StartupManager.SetEnabled(requested, Application.ExecutablePath);
                settings.StartWithWindows = StartupManager.IsEnabled();
                if (settings.StartWithWindows != requested)
                    throw new InvalidOperationException("Windows did not retain the requested startup setting.");
            }
            catch (Exception ex)
            {
                settings.StartWithWindows = previous;
                _loading = true;
                startup.Checked = previous;
                _loading = false;
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

        LayoutSection(general);
        root.Controls.Add(general);

        var behavior = MakeSection("Chat appearance");
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

        var profiles = MakeSection("Choose your apps");
        profiles.Controls.Add(new Label
        {
            Text = "Select the AI apps where you want RTL Fixer to work. Other detected apps are left untouched.",
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
        foreach (var profile in _orchestrator.Profiles
                     .Where(profile => profile.SupportsRuntimeInjection)
                     .OrderBy(profile => profile.DisplayName))
        {
            // Opt-in: a profile with no saved toggle yet has never been enabled by
            // the user, so the checkbox must start unchecked, not pre-ticked.
            var enabled = settings.Apps.TryGetValue(profile.AppId, out var toggle) && toggle.Enabled;
            profileList.Items.Add(new ProfileChoice(profile), enabled);
        }
        profileList.Height = Math.Clamp(profileList.Items.Count * profileList.ItemHeight + 6, 150, 280);
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

        var advancedToggle = new Button
        {
            Text = "Show advanced settings",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = "Show advanced settings",
            Margin = new Padding(0, 0, 0, 6),
        };
        var advanced = MakeSection("Advanced settings");
        advanced.Visible = false;
        advanced.Controls.Add(autoRelaunch);
        advanced.Controls.Add(browserTargets);
        advanced.Controls.Add(updateChecks);
        var checkUpdates = new Button { Text = "Check for updates now", AutoSize = true, AccessibleName = "Check for updates now", Margin = new Padding(0, 10, 0, 0) };
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
        advanced.Controls.Add(checkUpdates);
        LayoutSection(advanced);
        advancedToggle.Click += (_, _) =>
        {
            advanced.Visible = !advanced.Visible;
            advancedToggle.Text = advanced.Visible ? "Hide advanced settings" : "Show advanced settings";
            advancedToggle.AccessibleName = advancedToggle.Text;
        };
        root.Controls.Add(advancedToggle);
        root.Controls.Add(advanced);

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
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, AccessibleName = "Close settings" };
        footer.Controls.Add(restore);
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
            Text = "Choose your apps once; Persian stays readable and code stays LTR.",
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
        var selected = _orchestrator.Profiles.Count(profile =>
            profile.SupportsRuntimeInjection
            && _orchestrator.Settings.Apps.TryGetValue(profile.AppId, out var toggle)
            && toggle.Enabled);
        _statusValue.Text = !_orchestrator.Settings.GlobalEnabled
            ? "Paused — no app is being changed"
            : attached > 0
                ? $"Working in {attached} app{(attached == 1 ? string.Empty : "s")}"
                : selected == 0
                    ? "Choose at least one app below"
                    : "Ready — open a selected app";
        _statusValue.ForeColor = _orchestrator.Settings.GlobalEnabled ? Color.FromArgb(13, 148, 136) : Color.FromArgb(100, 116, 139);
    }

    private sealed record ProfileChoice(AppProfile Profile)
    {
        public override string ToString() => Profile.DisplayName;
    }
}
