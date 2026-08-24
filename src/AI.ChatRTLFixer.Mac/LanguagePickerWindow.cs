using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Localization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// macOS counterpart of the Windows first-run language picker.
///
/// <para>
/// Every language is listed in its OWN script and the list is never translated:
/// someone who cannot read the current interface language still has to
/// recognise their own, and "Persian" spelled in English does not help a
/// Persian speaker find it.
/// </para>
/// </summary>
public sealed class LanguagePickerWindow : Window
{
    private readonly TaskCompletionSource<string?> _result = new();

    /// <summary>Completes with the chosen code, or null if the window was closed.</summary>
    public Task<string?> Selection => _result.Task;

    public LanguagePickerWindow()
    {
        Title = Constants.ProductName;
        Width = 440;
        Height = 340;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var title = new TextBlock
        {
            Text = Loc.T("language.pick.title"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
        };
        var body = new TextBlock
        {
            Text = Loc.T("language.pick.body"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        };

        var list = new ListBox
        {
            Height = 170,
            ItemsSource = UiLanguages.All.Select(l => l.NativeName).ToList(),
            SelectedIndex = Math.Max(0, UiLanguages.All.ToList().FindIndex(l => l.Code == UiLanguages.DefaultCode)),
        };

        var confirm = new Button
        {
            Content = Loc.T("language.pick.confirm"),
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 110,
        };

        // Retitle live so the choice is confirmed in the language just picked.
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var language = UiLanguages.All[list.SelectedIndex];
            Loc.SetLanguage(language.Code);
            title.Text = Loc.T("language.pick.title");
            body.Text = Loc.T("language.pick.body");
            confirm.Content = Loc.T("language.pick.confirm");
            FlowDirection = language.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        };

        confirm.Click += (_, _) =>
        {
            var index = Math.Max(0, list.SelectedIndex);
            _result.TrySetResult(UiLanguages.All[index].Code);
            Close();
        };

        // Closing without choosing keeps the default and leaves the question to
        // be asked again next launch, rather than blocking startup.
        Closed += (_, _) => _result.TrySetResult(null);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(22),
            Spacing = 14,
            Children = { title, body, list, confirm },
        };

        FlowDirection = UiLanguages.Default.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
