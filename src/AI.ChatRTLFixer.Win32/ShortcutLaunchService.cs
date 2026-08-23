using System.Runtime.InteropServices;
using System.Text;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Win32;

/// <summary>
/// Writes the loopback debugging flags into the Windows shortcuts a target app
/// is launched from (Start menu, Desktop, taskbar pins), so the app comes up
/// with its endpoint already enabled and never has to be closed and reopened
/// again on later sessions.
///
/// <para>
/// Only per-user locations are rewritten. Machine-wide shortcuts under
/// ProgramData are reported as skipped rather than modified: editing them would
/// need elevation and would change the app for every account on the PC.
/// </para>
/// </summary>
public sealed class ShortcutLaunchService : IPersistentLaunchService
{
    private readonly SafeLogger _logger;

    public ShortcutLaunchService(SafeLogger logger) => _logger = logger;

    public IReadOnlyList<LaunchShortcut> FindShortcuts(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return [];
        var found = new List<LaunchShortcut>();
        foreach (var (root, location, writable) in SearchRoots())
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "shortcut-scan-failed",
                    ("location", location), ("msg", SafeLogger.Redact(ex.Message)));
                continue;
            }

            foreach (var file in files)
            {
                if (!TryRead(file, out var target, out var arguments)) continue;
                if (!PathsMatch(target, executablePath)) continue;
                found.Add(new LaunchShortcut
                {
                    Path = file,
                    Location = writable ? location : location + " (system-wide)",
                    Arguments = arguments,
                    HasDebugArguments = PersistentLaunchFlags.HasRemoteDebuggingArguments(
                        PersistentLaunchFlags.Tokenize(arguments)),
                });
            }
        }
        return found;
    }

    public PersistentLaunchResult Install(string executablePath, int port)
    {
        if (port is < 1 or > 65535)
            return new PersistentLaunchResult { Success = false, Detail = "invalid-port" };
        return Rewrite(executablePath, args => PersistentLaunchFlags.ApplyDebugArguments(args, port), port);
    }

    public PersistentLaunchResult Remove(string executablePath)
        => Rewrite(executablePath, PersistentLaunchFlags.RemoveDebugArguments, port: null);

    private PersistentLaunchResult Rewrite(string executablePath, Func<string, string> transform, int? port)
    {
        var shortcuts = FindShortcuts(executablePath);
        if (shortcuts.Count == 0)
            return new PersistentLaunchResult { Success = false, Detail = "no-shortcuts-found", Port = port };

        var updated = new List<LaunchShortcut>();
        var skipped = new List<LaunchShortcut>();
        foreach (var shortcut in shortcuts)
        {
            if (shortcut.Location.EndsWith("(system-wide)", StringComparison.Ordinal))
            {
                skipped.Add(shortcut);
                continue;
            }
            var next = transform(shortcut.Arguments);
            if (string.Equals(next, shortcut.Arguments, StringComparison.Ordinal))
            {
                // Already in the requested state; count it as done so a repeat
                // run reports success instead of looking like a failure.
                updated.Add(shortcut);
                continue;
            }
            if (TryWriteArguments(shortcut.Path, next))
            {
                updated.Add(new LaunchShortcut
                {
                    Path = shortcut.Path,
                    Location = shortcut.Location,
                    Arguments = next,
                    HasDebugArguments = port is not null,
                });
            }
            else
            {
                skipped.Add(shortcut);
            }
        }

        _logger.Log(LogLevel.Information, LogCategories.Relaunch, "persistent-launch-rewrite",
            ("updated", updated.Count), ("skipped", skipped.Count), ("port", port ?? 0));

        return new PersistentLaunchResult
        {
            Success = updated.Count > 0,
            Updated = updated,
            Skipped = skipped,
            Port = port,
            Detail = updated.Count > 0 ? null : "no-writable-shortcuts",
        };
    }

    private static IEnumerable<(string Root, string Location, bool Writable)> SearchRoots()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return (Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs"), "Start menu", true);
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop", true);
        yield return (Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), "Taskbar", true);
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Start menu", false);
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Desktop", false);
    }

    private static bool PathsMatch(string? shortcutTarget, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(shortcutTarget)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(shortcutTarget).TrimEnd('\\'),
                Path.GetFullPath(executablePath).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(shortcutTarget, executablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool TryRead(string lnkPath, out string target, out string arguments)
    {
        target = string.Empty;
        arguments = string.Empty;
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(lnkPath, StgmRead);
            var buffer = new StringBuilder(MaxPath);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            target = buffer.ToString();
            buffer.Clear();
            link.GetArguments(buffer, buffer.Capacity);
            arguments = buffer.ToString();
            Marshal.FinalReleaseComObject(link);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryWriteArguments(string lnkPath, string arguments)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            var file = (IPersistFile)link;
            file.Load(lnkPath, StgmReadWrite);
            link.SetArguments(arguments);
            file.Save(lnkPath, fRemember: true);
            Marshal.FinalReleaseComObject(link);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "shortcut-write-failed",
                ("msg", SafeLogger.Redact(ex.Message)));
            return false;
        }
    }

    private const int MaxPath = 260;
    private const int StgmRead = 0x00000000;
    private const int StgmReadWrite = 0x00000002;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }
}
