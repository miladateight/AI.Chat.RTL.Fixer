using Microsoft.Playwright;

namespace AI.ChatRTLFixer.IntegrationTests;

/// <summary>
/// Wraps a Playwright instance + browser, created once per test. Headless
/// Chromium mirrors the runtime inside Electron apps.
/// </summary>
public sealed class PlaywrightFixture : IDisposable
{
    public IPlaywright Pw { get; }
    public IBrowser Browser { get; }

    private PlaywrightFixture(IPlaywright pw, IBrowser b)
    {
        Pw = pw;
        Browser = b;
    }

    public static async Task<PlaywrightFixture> CreateAsync()
    {
        var pw = await Playwright.CreateAsync();
        try
        {
            var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return new PlaywrightFixture(pw, browser);
        }
        catch (PlaywrightException)
        {
            // A browser download may be unavailable in restricted regions.
            // Fall back to the installed stable Chrome channel while keeping
            // the test isolated and headless.
            var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Channel = "chrome",
            });
            return new PlaywrightFixture(pw, browser);
        }
    }

    public Task<IPage> NewPageAsync() => Browser.NewPageAsync();

    public void Dispose()
    {
        Browser?.CloseAsync().GetAwaiter().GetResult();
        Pw?.Dispose();
    }
}
