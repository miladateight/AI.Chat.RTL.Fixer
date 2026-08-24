using System.Diagnostics;
using AI.ChatRTLFixer.Win32;

namespace AI.ChatRTLFixer.Core.Tests;

/// <summary>
/// The relaunch path closes somebody's chat application. Every check here exists
/// because getting it wrong once left a user with an app that vanished and would
/// not come back, mid-conversation.
/// </summary>
public class RelaunchSafetyTests
{
    [Fact]
    public void CountOtherMainProcesses_IgnoresTheProcessItself()
    {
        using var self = Process.GetCurrentProcess();
        var path = self.MainModule?.FileName;
        Assert.False(string.IsNullOrEmpty(path), "cannot read own executable path");

        // The only process at this path in a test run is this one, so excluding
        // itself must leave nothing behind.
        Assert.Equal(0, RelaunchService.CountOtherMainProcesses(self.Id, path!));
    }

    [Fact]
    public void CountOtherMainProcesses_ReportsNoneForAnUnknownPath()
    {
        Assert.Equal(0, RelaunchService.CountOtherMainProcesses(
            selfPid: 0, exe: @"C:\definitely\not\here\NoSuchApp.exe"));
    }

    [Fact]
    public void CountOtherMainProcesses_DoesNotThrowOnAnEmptyPath()
    {
        // Called on the relaunch path before anything is closed; an exception
        // here would surface as a failed relaunch on an app already killed.
        var ex = Record.Exception(() => RelaunchService.CountOtherMainProcesses(0, string.Empty));
        Assert.Null(ex);
    }

    /// <summary>
    /// Expected values verified against what Get-AppxPackage reports for these
    /// two real installs. If this parsing is wrong the rescue activation
    /// silently does nothing and the user is left with a closed app.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.818.5345.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe", "OpenAI.Codex_2p2nqsd0c76g0")]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_1.34493.1.0_x64__pzs8sxrjxfjjc\app\Claude.exe", "Claude_pzs8sxrjxfjjc")]
    public void PackageFamilyName_IsDerivedFromTheInstallPath(string path, string expected)
    {
        Assert.Equal(expected, PackagedAppLauncher.TryGetPackageFamilyName(path));
    }

    [Theory]
    [InlineData(@"C:\Users\Milad\AppData\Local\Programs\ZCode\ZCode.exe")]
    [InlineData(@"C:\Program Files\AI RTL Fixer\AI.ChatRTLFixer.Tray.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void PackageFamilyName_IsNullForOrdinaryInstalls(string? path)
    {
        Assert.Null(PackagedAppLauncher.TryGetPackageFamilyName(path));
    }

    [Fact]
    public void TryActivate_RefusesAPathThatIsNotAPackagedApp()
    {
        // Must not attempt any launch at all for a non-packaged path.
        Assert.False(PackagedAppLauncher.TryActivate(@"C:\nope\App.exe"));
    }

    [Fact]
    public void IsAnyInstanceRunning_FindsThisTestProcess()
    {
        using var self = Process.GetCurrentProcess();
        Assert.True(PackagedAppLauncher.IsAnyInstanceRunning(self.MainModule!.FileName));
    }

    [Fact]
    public void IsAnyInstanceRunning_IsFalseForAnAppThatIsNotRunning()
    {
        Assert.False(PackagedAppLauncher.IsAnyInstanceRunning(@"C:\nope\NoSuchAppAnywhere.exe"));
    }

    /// <summary>
    /// Remembered consent must never reach an app that was already open when the
    /// fixer started. Consent was one click for one relaunch; treating it as
    /// standing permission meant simply launching the fixer closed a chat the
    /// user was in the middle of, at every startup, with no click involved.
    /// </summary>
    [Fact]
    public void DetectedApp_DefaultsToNotAlreadyRunning()
    {
        var app = new AI.ChatRTLFixer.Core.Abstractions.DetectedApp
        {
            AppId = "x", ProcessId = 1, ProcessName = "x",
        };
        // Default false: only the watcher's first scan sets it, so a mistake in
        // wiring fails towards asking rather than towards closing.
        Assert.False(app.WasAlreadyRunning);
    }

    [Fact]
    public void DetectedApp_CarriesTheAlreadyRunningFlag()
    {
        var app = new AI.ChatRTLFixer.Core.Abstractions.DetectedApp
        {
            AppId = "x", ProcessId = 1, ProcessName = "x", WasAlreadyRunning = true,
        };
        Assert.True(app.WasAlreadyRunning);
    }
}
