using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Localization;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Compact settings surface shared conceptually with Windows: the common
/// choices stay visible and technical controls live in a collapsed section.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Orchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly SafeLogger _logger;
    private readonly UpdateChecker _updateChecker;
    private readonly TextBlock _statusValue = new() { FontWeight = FontWeight.Bold, FontSize = 12 };
    private bool _loading;

    public SettingsWindow() { InitializeComponent(); _orchestrator = null!; _settingsStore = null!; _logger = null!; _updateChecker = null!; }

    public SettingsWindow(Orchestrator orchestrator, ISettingsStore settingsStore, SafeLogger logger, UpdateChecker updateChecker)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _logger = logger;
        _updateChecker = updateChecker;
        InitializeComponent();
        // Mirror the whole window, not just the text, for an RTL language.
        FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Build();
    }

    private void Build()
    {
        _loading = true;
        var settings = _orchestrator.Settings;

        var root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        var scroll = new ScrollViewer { Content = root };
        Content = scroll;

        root.Children.Add(BuildHeader());

        var general = MakeSection(Loc.T("section.quickSetup"), out var generalBody);

        var languageRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        languageRow.Children.Add(new TextBlock { Text = Loc.T("language.label"), VerticalAlignment = VerticalAlignment.Center });
        var languageBox = new ComboBox
        {
            Width = 200,
            ItemsSource = UiLanguages.All.Select(l => l.NativeName).ToList(),
            SelectedIndex = Math.Max(0, UiLanguages.All.ToList().FindIndex(l => l.Code == Loc.Current.Code)),
        };
        languageBox.SelectionChanged += async (_, _) =>
        {
            if (_loading || languageBox.SelectedIndex < 0) return;
            var chosen = UiLanguages.All[languageBox.SelectedIndex];
            if (chosen.Code == settings.UiCulture) return;
            settings.UiCulture = chosen.Code;
            Loc.SetLanguage(chosen.Code);
            await SaveAsync();
            // Rebuild rather than retranslate: switching between an RTL and an
            // LTR language changes the direction of every control in the window.
            FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Build();
        };
        languageRow.Children.Add(languageBox);
        generalBody.Children.Add(languageRow);
        var global = new CheckBox { Content = Loc.T("toggle.enable"), IsChecked = settings.GlobalEnabled };
        global.IsCheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            await _orchestrator.SetGlobalEnabledAsync(global.IsChecked == true);
            await SaveAsync();
            UpdateStatus();
        };
        generalBody.Children.Add(global);

        var startup = new CheckBox { Content = Loc.T("toggle.startWithMac"), IsChecked = settings.StartWithWindows };
        startup.IsCheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            var previous = settings.StartWithWindows;
            var requested = startup.IsChecked == true;
            try
            {
                StartupManager.SetEnabled(requested, Environment.ProcessPath ?? string.Empty);
                settings.StartWithWindows = StartupManager.IsEnabled();
                if (settings.StartWithWindows != requested)
                    throw new InvalidOperationException("macOS did not retain the requested login setting.");
            }
            catch (Exception ex)
            {
                settings.StartWithWindows = previous;
                _loading = true;
                startup.IsChecked = previous;
                _loading = false;
                _logger.Log(LogLevel.Warning, LogCategories.App, "startup-set-failed", ("msg", SafeLogger.Redact(ex.Message)));
                Dialogs.Warn(Title!, Loc.T("startup.failed"));
            }
            await SaveAsync();
        };
        generalBody.Children.Add(startup);

        var autoRelaunch = new CheckBox { Content = Loc.T("toggle.rememberRelaunch"), IsChecked = settings.AutoRelaunchAfterConsent };
        autoRelaunch.IsCheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            settings.AutoRelaunchAfterConsent = autoRelaunch.IsChecked == true;
            await SaveAsync();
        };

        var browserTargets = new CheckBox { Content = Loc.T("toggle.browserTargets"), IsChecked = settings.EnableBrowserTargets };
        browserTargets.IsCheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            if (browserTargets.IsChecked == true)
            {
                var confirmed = await Dialogs.ConfirmAsync(Title!,
                    Loc.T("browser.confirm"));
                if (!confirmed)
                {
                    _loading = true;
                    browserTargets.IsChecked = false;
                    _loading = false;
                    return;
                }
            }
            await _orchestrator.SetBrowserTargetsEnabledAsync(browserTargets.IsChecked == true);
            await SaveAsync();
            UpdateStatus();
        };

        var updateChecks = new CheckBox { Content = Loc.T("toggle.updateCheck"), IsChecked = settings.CheckForUpdatesOnStartup };
        updateChecks.IsCheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            settings.CheckForUpdatesOnStartup = updateChecks.IsChecked == true;
            await SaveAsync();
        };
        root.Children.Add(general);

        var behavior = MakeSection(Loc.T("section.appearance"), out var behaviorBody);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 8 };

        var fontLabel = new TextBlock { Text = Loc.T("label.font"), VerticalAlignment = VerticalAlignment.Center };
        var font = new ComboBox { Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        var availableFonts = Enum.GetValues<FontChoice>().Where(choice => choice != FontChoice.Custom).ToArray();
        foreach (var choice in availableFonts) font.Items.Add(choice);
        font.SelectedItem = availableFonts.Contains(settings.SelectedFont) ? settings.SelectedFont : FontChoice.Vazirmatn;
        font.SelectionChanged += async (_, _) =>
        {
            if (_loading || font.SelectedItem is not FontChoice selected) return;
            settings.SelectedFont = selected;
            await _orchestrator.RefreshAttachedAsync();
            await SaveAsync();
        };

        var copyLabel = new TextBlock { Text = Loc.T("label.copyMode"), VerticalAlignment = VerticalAlignment.Center };
        var copy = new ComboBox { Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var choice in Enum.GetValues<CopyMode>()) copy.Items.Add(choice);
        copy.SelectedItem = settings.CopyMode;
        copy.SelectionChanged += async (_, _) =>
        {
            if (_loading || copy.SelectedItem is not CopyMode selected) return;
            settings.CopyMode = selected;
            await _orchestrator.RefreshAttachedAsync();
            await SaveAsync();
        };

        Grid.SetColumn(fontLabel, 0); Grid.SetRow(fontLabel, 0);
        Grid.SetColumn(font, 1); Grid.SetRow(font, 0);
        Grid.SetColumn(copyLabel, 0); Grid.SetRow(copyLabel, 1);
        Grid.SetColumn(copy, 1); Grid.SetRow(copy, 1);
        grid.Children.Add(fontLabel); grid.Children.Add(font); grid.Children.Add(copyLabel); grid.Children.Add(copy);
        behaviorBody.Children.Add(grid);
        root.Children.Add(behavior);

        var profilesSection = MakeSection(Loc.T("section.chooseApps"), out var profilesBody);
        profilesBody.Children.Add(new TextBlock
        {
            Text = Loc.T("text.chooseAppsHelp"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        });
        var profileList = new StackPanel { Spacing = 6 };
        foreach (var profile in _orchestrator.Profiles
                     .Where(profile => profile.SupportsRuntimeInjection)
                     .OrderBy(profile => profile.DisplayName))
        {
            var enabled = settings.Apps.TryGetValue(profile.AppId, out var toggle) && toggle.Enabled;
            var checkBox = new CheckBox { Content = profile.DisplayName, IsChecked = enabled, Tag = profile };
            checkBox.IsCheckedChanged += async (_, _) =>
            {
                if (_loading) return;
                await _orchestrator.SetAppEnabledAsync(profile.AppId, checkBox.IsChecked == true);
                await SaveAsync();
                UpdateStatus();
            };
            profileList.Children.Add(checkBox);
        }
        var profileScroll = new ScrollViewer { Content = profileList, MaxHeight = 260, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        profilesBody.Children.Add(profileScroll);
        root.Children.Add(profilesSection);

        var advancedBody = new StackPanel { Spacing = 10 };
        advancedBody.Children.Add(autoRelaunch);
        advancedBody.Children.Add(browserTargets);
        advancedBody.Children.Add(updateChecks);
        var checkUpdates = new Button { Content = Loc.T("button.checkUpdates"), HorizontalAlignment = HorizontalAlignment.Left };
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
        advancedBody.Children.Add(checkUpdates);
        root.Children.Add(new Expander
        {
            Header = Loc.T("section.advanced"),
            IsExpanded = false,
            Content = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 6, 0, 0),
                Child = advancedBody,
            },
        });

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        var restore = new Button { Content = Loc.T("button.restorePause") };
        restore.Click += async (_, _) =>
        {
            await _orchestrator.SetGlobalEnabledAsync(false);
            _loading = true;
            global.IsChecked = false;
            _loading = false;
            await SaveAsync();
            UpdateStatus();
        };
        var close = new Button { Content = Loc.T("button.close") };
        close.Click += (_, _) => Close();
        footer.Children.Add(restore);
        footer.Children.Add(close);
        root.Children.Add(footer);

        _loading = false;
        UpdateStatus();
    }

    private Control BuildHeader()
    {
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(new TextBlock { Text = Constants.ProductName, FontSize = 18, FontWeight = FontWeight.Bold });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("app.tagline"),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        });
        header.Children.Add(_statusValue);
        return header;
    }

    private static Border MakeSection(string title, out StackPanel body)
    {
        body = new StackPanel { Spacing = 10 };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(body);
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            Child = content,
        };
    }

    private async Task SaveAsync() => await _settingsStore.SaveAsync(_orchestrator.Settings, CancellationToken.None);

    private async Task CheckForUpdatesAsync()
    {
        var result = await _updateChecker.CheckAsync(CancellationToken.None);
        if (result.IsUpdateAvailable)
        {
            var open = await Dialogs.ConfirmAsync(Constants.ProductName, Loc.T("update.available", result.LatestVersion?.ToString() ?? string.Empty));
            if (open && result.ReleasePage is not null) UpdateChecker.OpenReleasePage(result.ReleasePage);
            return;
        }
        Dialogs.Info(Constants.ProductName, result.Message);
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
        _statusValue.Foreground = new SolidColorBrush(_orchestrator.Settings.GlobalEnabled ? Color.FromRgb(13, 148, 136) : Color.FromRgb(100, 116, 139));
    }
}
