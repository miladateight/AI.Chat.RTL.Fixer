using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Localization;
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
    private GroupBox? _attention;
    private Label? _attentionText;
    private Button? _relaunchButton;
    private bool _loading;

    public SettingsForm(Orchestrator orchestrator, ISettingsStore settingsStore, SafeLogger logger, UpdateChecker updateChecker)
    {
        _orchestrator = orchestrator;
        // The window is usually already open when an app is detected, so the
        // "needs attention" banner has to appear on its own rather than only
        // when the user next clicks something.
        _orchestrator.StateChanged += OnOrchestratorStateChanged;
        _settingsStore = settingsStore;
        _logger = logger;
        _updateChecker = updateChecker;

        Text = Constants.ProductName;
        Font = new Font("Segoe UI", 9F);
        RightToLeft = Loc.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = Loc.IsRtl;
        ClientSize = new Size(600, 720);
        MinimumSize = new Size(600, 460);
        // Sizable, not FixedDialog: the content grew past a fixed height and a
        // dialog the user cannot resize simply clipped the controls at the
        // bottom with no way to reach them.
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);
        Build();
    }

    private void Build()
    {
        _loading = true;
        var settings = _orchestrator.Settings;
        // WinForms docks the HIGHEST-indexed control first, so a Fill control has
        // to be added BEFORE the edge-docked ones to end up with the leftover
        // space. Added the other way round, Fill claimed the whole client area
        // and the button strip was squeezed to nothing — which is why the
        // controls at the bottom could be neither seen nor clicked.
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

        // Action buttons in a strip pinned to the bottom, so no amount of
        // content above can push them out of reach. Only the panel above scrolls.
        var bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(20, 8, 20, 8),
            BackColor = Color.FromArgb(241, 245, 249),
        };
        Controls.Add(bottomBar);

        root.Controls.Add(BuildHeader());

        // Anything blocking the fix sits directly under the header, with the
        // action on it. Previously the only way to act was to find "Relaunch
        // with RTL Fix" in the tray menu, which is not where someone looks when
        // the app they just opened is not being fixed.
        _attention = MakeSection(Loc.T("section.needsAttention"), out var attentionBody);
        _attentionText = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(SectionInnerWidth, 0),
            ForeColor = Color.FromArgb(120, 53, 15),
        };
        _relaunchButton = new Button
        {
            Text = Loc.T("button.relaunchNow"),
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            AccessibleName = Loc.T("button.relaunchNow"),
        };
        _relaunchButton.Click += async (_, _) => await RelaunchPendingAsync();
        Add(attentionBody, _attentionText);
        Add(attentionBody, _relaunchButton);
        _attention.BackColor = Color.FromArgb(254, 249, 231);
        FinishSection(_attention, attentionBody);
        root.Controls.Add(_attention);

        var general = MakeSection(Loc.T("section.quickSetup"), out var generalBody);

        var languageRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 4),
        };
        languageRow.Controls.Add(new Label
        {
            Text = Loc.T("language.label"),
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0),
        });
        var language = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
            AccessibleName = Loc.T("language.label"),
        };
        foreach (var option in UiLanguages.All) language.Items.Add(new LanguageChoice(option));
        language.SelectedIndex = Math.Max(0, UiLanguages.All
            .ToList().FindIndex(l => l.Code == Loc.Current.Code));
        language.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading || language.SelectedItem is not LanguageChoice choice) return;
            if (choice.Language.Code == settings.UiCulture) return;
            settings.UiCulture = choice.Language.Code;
            Loc.SetLanguage(choice.Language.Code);
            await SaveAsync();
            // Rebuild rather than retranslate in place: switching between an RTL
            // and an LTR language changes the layout direction of every control,
            // which WinForms only applies cleanly when they are created again.
            SuspendLayout();
            Controls.Clear();
            Build();
            ResumeLayout(performLayout: true);
        };
        languageRow.Controls.Add(language);
        Add(generalBody, languageRow);
        var global = new CheckBox
        {
            Text = Loc.T("toggle.enable"),
            Checked = settings.GlobalEnabled,
            AutoSize = true,
            AccessibleName = Loc.T("toggle.enable"),
        };
        global.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            await _orchestrator.SetGlobalEnabledAsync(global.Checked);
            await SaveAsync();
            UpdateStatus();
        };
        Add(generalBody, global);

        var startup = new CheckBox
        {
            Text = Loc.T("toggle.startWithWindows"),
            Checked = settings.StartWithWindows,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = Loc.T("toggle.startWithWindows"),
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
                MessageBox.Show(Loc.T("startup.failed"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            await SaveAsync();
        };
        // The packaged build cannot honour this control — see Program.Main — and a
        // checkbox that reports success while Windows ignores it is worse than no
        // checkbox. Store users get the startup entry the package declares, which
        // Windows lists under Settings > Apps > Startup like every other packaged
        // app.
        if (!PackageContext.IsPackaged)
            Add(generalBody, startup);

        FinishSection(general, generalBody);

        var autoRelaunch = new CheckBox
        {
            Text = Loc.T("toggle.rememberRelaunch"),
            Checked = settings.AutoRelaunchAfterConsent,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = Loc.T("toggle.rememberRelaunch"),
        };
        autoRelaunch.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            settings.AutoRelaunchAfterConsent = autoRelaunch.Checked;
            await SaveAsync();
        };

        var browserTargets = new CheckBox
        {
            Text = Loc.T("toggle.browserTargets"),
            Checked = settings.EnableBrowserTargets,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = Loc.T("toggle.browserTargets"),
        };
        browserTargets.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            if (browserTargets.Checked && MessageBox.Show(
                    Loc.T("browser.confirm"),
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
            Text = Loc.T("toggle.updateCheck"),
            Checked = settings.CheckForUpdatesOnStartup,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = Loc.T("toggle.updateCheck"),
        };
        updateChecks.CheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            settings.CheckForUpdatesOnStartup = updateChecks.Checked;
            await SaveAsync();
        };

        root.Controls.Add(general);

        var behavior = MakeSection(Loc.T("section.appearance"), out var behaviorBody);
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

        var fontLabel = new Label { Text = Loc.T("label.font"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 7) };
        var font = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 250,
            AccessibleName = Loc.T("label.font"),
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

        var copyLabel = new Label { Text = Loc.T("label.copyMode"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 7) };
        var copy = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 250,
            AccessibleName = Loc.T("label.copyMode"),
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
        Add(behaviorBody, behaviorGrid);
        FinishSection(behavior, behaviorBody);
        root.Controls.Add(behavior);

        var profiles = MakeSection(Loc.T("section.chooseApps"), out var profilesBody);
        Add(profilesBody, new Label
        {
            Text = Loc.T("text.chooseAppsHelp"),
            AutoSize = true,
            MaximumSize = new Size(SectionInnerWidth, 0),
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
            AccessibleName = Loc.T("section.chooseApps"),
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
        Add(profilesBody, profileList);
        FinishSection(profiles, profilesBody);
        root.Controls.Add(profiles);

        var advancedToggle = new Button
        {
            Text = Loc.T("button.showAdvanced"),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = Loc.T("button.showAdvanced"),
            Margin = new Padding(0, 0, 0, 6),
        };
        var advanced = MakeSection(Loc.T("section.advanced"), out var advancedBody);
        advanced.Visible = false;
        Add(advancedBody, autoRelaunch);
        Add(advancedBody, browserTargets);

        // The Store keeps its own copy of the app up to date, and a Store app that
        // sends users to fetch an installer from somewhere else is both confusing
        // and against store policy. So the packaged build does not offer update
        // checking at all rather than offering it and then declining to act.
        if (!PackageContext.IsPackaged)
        {
            Add(advancedBody, updateChecks);
            var checkUpdates = new Button { Text = Loc.T("button.checkUpdates"), AutoSize = true, AccessibleName = Loc.T("button.checkUpdates"), Margin = new Padding(0, 10, 0, 0) };
            checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
            Add(advancedBody, checkUpdates);
        }

        FinishSection(advanced, advancedBody);
        advancedToggle.Click += (_, _) =>
        {
            advanced.Visible = !advanced.Visible;
            advancedToggle.Text = advanced.Visible ? Loc.T("button.hideAdvanced") : Loc.T("button.showAdvanced");
            advancedToggle.AccessibleName = advancedToggle.Text;
        };
        root.Controls.Add(advancedToggle);
        root.Controls.Add(advanced);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
        };
        var restore = new Button { Text = Loc.T("button.restorePause"), AutoSize = true, AccessibleName = Loc.T("button.restorePause") };
        restore.Click += async (_, _) =>
        {
            await _orchestrator.SetGlobalEnabledAsync(false);
            _loading = true;
            global.Checked = false;
            _loading = false;
            await SaveAsync();
            UpdateStatus();
        };
        var close = new Button { Text = Loc.T("button.close"), AutoSize = true, DialogResult = DialogResult.OK, AccessibleName = Loc.T("button.close") };
        footer.Controls.Add(restore);
        footer.Controls.Add(close);
        bottomBar.Controls.Add(footer);
        CancelButton = close;

        _loading = false;
        UpdateStatus();
    }

    private Control BuildHeader()
    {
        var header = new Panel { Width = 520, Height = 76, Margin = new Padding(0, 0, 0, 12) };
        header.Controls.Add(new Label
        {
            Text = Constants.ProductName,
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 0),
        });
        header.Controls.Add(new Label
        {
            Text = Loc.T("app.tagline"),
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

    private const int SectionWidth = 520;
    private const int SectionInnerWidth = SectionWidth - 40;

    /// <summary>
    /// A titled section whose children stack vertically.
    /// </summary>
    /// <remarks>
    /// Children go into a FlowLayoutPanel instead of being positioned by hand.
    /// Manual <c>Location = new Point(14, y)</c> arithmetic encodes a
    /// left-to-right assumption: under RightToLeftLayout those coordinates are
    /// mirrored, and every control ended up hanging past the right edge of the
    /// window where it could be neither seen nor clicked — in Persian, Arabic,
    /// Hebrew and Urdu, but never in English, which is why it shipped. A flow
    /// panel mirrors itself, so one code path is correct in all five.
    /// </remarks>
    private static GroupBox MakeSection(string title, out FlowLayoutPanel body)
    {
        var section = new GroupBox
        {
            Text = title,
            Width = SectionWidth,
            // AutoSize on a GroupBox does not measure a DOCKED child, which
            // collapsed every section to a sliver. The height is set explicitly
            // by FinishSection once the children are in.
            AutoSize = false,
            Padding = new Padding(14, 24, 14, 14),
            Margin = new Padding(0, 0, 0, 12),
        };
        body = new FlowLayoutPanel
        {
            Location = new Point(14, 24),
            // Pin the width and let only the height grow, so the flow panel
            // wraps its children instead of widening past the section.
            MinimumSize = new Size(SectionInnerWidth, 0),
            MaximumSize = new Size(SectionInnerWidth, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
        };
        section.Controls.Add(body);
        return section;
    }

    /// <summary>Sizes a section to the content that was added to it.</summary>
    private static void FinishSection(GroupBox section, FlowLayoutPanel body)
        => section.Height = body.Top + Math.Max(body.Height, body.PreferredSize.Height) + 14;

    /// <summary>
    /// Adds a control to a section, capping its width so long sentences wrap
    /// rather than running past the edge. A Persian or Arabic label is several
    /// times the length of its English original, so an AutoSize control that
    /// looked right in English overflowed everywhere else.
    /// </summary>
    private static T Add<T>(FlowLayoutPanel body, T control) where T : Control
    {
        if (control.AutoSize) control.MaximumSize = new Size(SectionInnerWidth, 0);
        else if (control.Width > SectionInnerWidth) control.Width = SectionInnerWidth;
        body.Controls.Add(control);
        return control;
    }

    private void OnOrchestratorStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(UpdateStatus); } catch (ObjectDisposedException) { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _orchestrator.StateChanged -= OnOrchestratorStateChanged;
        base.OnFormClosed(e);
    }

    private async Task SaveAsync() => await _settingsStore.SaveAsync(_orchestrator.Settings, CancellationToken.None);

    private async Task CheckForUpdatesAsync()
    {
        var result = await _updateChecker.CheckAsync(CancellationToken.None);
        if (result.IsUpdateAvailable)
        {
            var open = MessageBox.Show(
                Loc.T("update.available", result.LatestVersion?.ToString() ?? string.Empty),
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
            ? Loc.T("status.paused")
            : attached > 0
                ? Loc.T("status.working", attached)
                : selected == 0
                    ? Loc.T("status.chooseApp")
                    : Loc.T("status.ready");
        _statusValue.ForeColor = _orchestrator.Settings.GlobalEnabled ? Color.FromArgb(13, 148, 136) : Color.FromArgb(100, 116, 139);
        UpdateAttention();
    }

    /// <summary>
    /// Shows or hides the banner that explains why an open app is not being
    /// fixed yet. It names the app, says why a relaunch is needed rather than
    /// just demanding one, and carries the button that performs it.
    /// </summary>
    private void UpdateAttention()
    {
        if (_attention is null || _attentionText is null || _relaunchButton is null) return;

        var pending = _orchestrator.PendingRelaunch;
        if (pending.Count == 0 || !_orchestrator.Settings.GlobalEnabled)
        {
            _attention.Visible = false;
            return;
        }

        var names = pending
            .Select(app => _orchestrator.Profiles.FirstOrDefault(p => p.AppId == app.AppId)?.DisplayName ?? app.AppId)
            .Distinct()
            .ToList();

        var headline = names.Count == 1
            ? Loc.T("relaunch.needed.one", names[0])
            : Loc.T("relaunch.needed.many", names.Count);

        _attentionText.Text = headline + "\n" + Loc.T("relaunch.why", string.Join(Loc.T("list.separator"), names));
        // The banner is laid out while its text is still empty, so it has to be
        // measured again now that it has content — otherwise the section keeps
        // its empty height and swallows the button underneath.
        FinishSection(_attention, (FlowLayoutPanel)_attention.Controls[0]);
        _attention.Visible = true;
    }

    /// <summary>
    /// Relaunches the apps that are detected but cannot be reached yet. The
    /// confirmation still names the app and still warns about unsaved work: this
    /// button is a shortcut to the existing consent flow, not a way around it.
    /// </summary>
    private async Task RelaunchPendingAsync()
    {
        var pending = _orchestrator.PendingRelaunch.ToList();
        foreach (var app in pending)
        {
            var display = _orchestrator.Profiles.FirstOrDefault(p => p.AppId == app.AppId)?.DisplayName ?? app.AppId;
            Task<bool> Consent(RelaunchWarning warning)
            {
                var body = Loc.T("relaunch.why", display) + "\n\n" + Loc.T("relaunch.warnUnsaved", display);
                var answer = MessageBox.Show(
                    Loc.T("relaunch.confirmBody", body),
                    Loc.T("relaunch.confirmTitle", display),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return Task.FromResult(answer == DialogResult.Yes);
            }

            var result = await _orchestrator.RelaunchAsync(app, Consent);
            if (result.Success)
                MessageBox.Show(Loc.T("relaunch.done", display), Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (result.ManualReopen || result.UserConsented)
                MessageBox.Show(
                    TrayApplicationContext.ExplainRelaunchFailure(display, result.Detail),
                    Loc.T("relaunch.manualTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateStatus();
    }

    private sealed record ProfileChoice(AppProfile Profile)
    {
        public override string ToString() => Profile.DisplayName;
    }

    private sealed record LanguageChoice(UiLanguage Language)
    {
        // Native name only, so the list stays readable whatever language the
        // rest of the window is currently in.
        public override string ToString() => Language.NativeName;
    }
}
