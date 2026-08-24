using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Localization;

namespace AI.ChatRTLFixer.Tray;

/// <summary>
/// Shown once, on the first run, before anything else.
///
/// <para>
/// Every language is listed in its OWN script, and the list is not translated
/// as the selection changes. Somebody who cannot read the current interface
/// language still has to be able to find their own — "فارسی" is recognisable to
/// a Persian speaker whatever the app happens to be showing at that moment,
/// while "Persian" is not.
/// </para>
/// </summary>
public sealed class LanguagePickerForm : Form
{
    public string SelectedCode { get; private set; } = UiLanguages.DefaultCode;

    public LanguagePickerForm()
    {
        Text = Constants.ProductName;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(420, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);

        var title = new Label
        {
            Text = Loc.T("language.pick.title"),
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 18),
        };

        var body = new Label
        {
            Text = Loc.T("language.pick.body"),
            AutoSize = false,
            Size = new Size(378, 40),
            ForeColor = Color.FromArgb(71, 85, 105),
            Location = new Point(20, 50),
        };

        var list = new ListBox
        {
            Location = new Point(20, 96),
            Size = new Size(378, 150),
            IntegralHeight = false,
            Font = new Font(Font.FontFamily, 11F),
            AccessibleName = Loc.T("language.label"),
        };
        foreach (var language in UiLanguages.All) list.Items.Add(new Choice(language));
        list.SelectedIndex = Math.Max(0, UiLanguages.All.ToList().FindIndex(l => l.Code == UiLanguages.DefaultCode));

        // Retitle live so the choice is confirmed in the language just picked —
        // the clearest possible signal that the selection landed.
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is not Choice choice) return;
            Loc.SetLanguage(choice.Language.Code);
            title.Text = Loc.T("language.pick.title");
            body.Text = Loc.T("language.pick.body");
            ApplyDirection(choice.Language.IsRtl);
        };

        var confirm = new Button
        {
            Text = Loc.T("language.pick.confirm"),
            Size = new Size(120, 32),
            Location = new Point(278, 262),
            DialogResult = DialogResult.OK,
        };
        confirm.Click += (_, _) =>
        {
            if (list.SelectedItem is Choice choice) SelectedCode = choice.Language.Code;
        };

        Controls.AddRange([title, body, list, confirm]);
        AcceptButton = confirm;

        // The picker itself opens in the default language, so it starts mirrored.
        ApplyDirection(UiLanguages.Default.IsRtl);
        list.SelectedIndexChanged += (_, _) => confirm.Text = Loc.T("language.pick.confirm");
    }

    private void ApplyDirection(bool rtl)
    {
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
    }

    private sealed record Choice(UiLanguage Language)
    {
        // Native name only: this list must stay readable to someone who cannot
        // read whatever language the app is currently showing.
        public override string ToString() => Language.NativeName;
    }
}
