using Avalonia.Controls;
using Avalonia.Layout;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Avalonia has no built-in MessageBox (unlike WinForms). These tiny modal-ish
/// windows cover the same three shapes the Windows tray uses: an info dialog,
/// a yes/no confirmation, and a warning confirmation.
/// </summary>
internal static class Dialogs
{
    public static void Info(string title, string message) => ShowInfo(title, message);

    public static void Warn(string title, string message) => ShowInfo(title, message);

    public static Task<bool> ConfirmAsync(string title, string message, string yesText = "Yes", string noText = "No")
    {
        var tcs = new TaskCompletionSource<bool>();
        var window = BuildWindow(title, message, out var body);

        var yes = new Button { Content = yesText, IsDefault = true, MinWidth = 84 };
        var no = new Button { Content = noText, IsCancel = true, MinWidth = 84 };
        yes.Click += (_, _) => { tcs.TrySetResult(true); window.Close(); };
        no.Click += (_, _) => { tcs.TrySetResult(false); window.Close(); };
        window.Closed += (_, _) => tcs.TrySetResult(false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(no);
        buttons.Children.Add(yes);
        body.Children.Add(buttons);

        window.Show();
        window.Activate();
        return tcs.Task;
    }

    private static void ShowInfo(string title, string message)
    {
        var window = BuildWindow(title, message, out var body);
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => window.Close();
        body.Children.Add(ok);
        window.Show();
        window.Activate();
    }

    private static Window BuildWindow(string title, string message, out StackPanel body)
    {
        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
        };
        body = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        window.Content = body;
        return window;
    }
}
