using Avalonia;

namespace AI.ChatRTLFixer.Mac;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instanceMutex = new Mutex(initiallyOwned: true, "AIChatRTLFixerMac", out var isFirstInstance);
        if (!isFirstInstance) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
