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
        var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        return new PlaywrightFixture(pw, browser);
    }

    public Task<IPage> NewPageAsync() => Browser.NewPageAsync();

    public void Dispose()
    {
        Browser?.CloseAsync().GetAwaiter().GetResult();
        Pw?.Dispose();
    }
}