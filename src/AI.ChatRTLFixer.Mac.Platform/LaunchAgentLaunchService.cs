using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// macOS counterpart of the Windows shortcut rewriter: starts a target app with
/// its loopback debugging flags already present, so the fixer attaches without
/// closing and reopening anything.
///
/// <para>
/// macOS has no equivalent of a Windows .lnk carrying arguments — Launch
/// Services starts an .app bundle without argv, so there is nothing on the Dock
/// icon to edit. The supported per-user mechanism is a LaunchAgent under
/// <c>~/Library/LaunchAgents</c>, the same approach this app already uses for
/// its own "start at login". The agent launches the target app with the flags
/// at login, which covers the way a chat app is normally used: started once per
/// session and left running.
/// </para>
///
/// <para>
/// LIMITATION, and it is deliberate that we do not work around it: if the user
/// quits the app and reopens it from the Dock or Spotlight, Launch Services
/// starts the bundle directly and that start carries no flags, so that session
/// still needs a relaunch. Covering it would mean rewriting the target app's own
/// bundle to point at a wrapper binary, which breaks its code signature. This
/// app does not modify other applications' bundles.
/// </para>
/// </summary>
public sealed class LaunchAgentLaunchService : IPersistentLaunchService
{
    private const string LabelPrefix = "com.aichatrtlfixer.persist";
    private readonly SafeLogger _logger;

    public LaunchAgentLaunchService(SafeLogger logger) => _logger = logger;

    private static string AgentsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "LaunchAgents");

    /// <summary>
    /// Label for an executable. Derived from the path so install and remove
    /// agree without needing the app id threaded through the interface.
    /// </summary>
    internal static string LabelFor(string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath) ?? "app";
        var safe = Regex.Replace(name, "[^A-Za-z0-9]", "-").Trim('-').ToLowerInvariant();
        if (safe.Length == 0) safe = "app";
        return $"{LabelPrefix}.{safe}";
    }

    private static string PlistPathFor(string executablePath)
        => Path.Combine(AgentsDirectory, LabelFor(executablePath) + ".plist");

    public IReadOnlyList<LaunchShortcut> FindShortcuts(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return [];
        var path = PlistPathFor(executablePath);
        if (!File.Exists(path)) return [];
        string content;
        try { content = File.ReadAllText(path); }
        catch { return []; }

        var arguments = string.Join(' ', ParseProgramArguments(content).Skip(1));
        return
        [
            new LaunchShortcut
            {
                Path = path,
                Location = "Login item",
                Arguments = arguments,
                HasDebugArguments = PersistentLaunchFlags.HasRemoteDebuggingArguments(
                    PersistentLaunchFlags.Tokenize(arguments)),
            }
        ];
    }

    public PersistentLaunchResult Install(string executablePath, int port)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return new PersistentLaunchResult { Success = false, Detail = "no-executable-path" };
        if (port is < 1 or > 65535)
            return new PersistentLaunchResult { Success = false, Detail = "invalid-port", Port = port };

        var path = PlistPathFor(executablePath);
        var arguments = PersistentLaunchFlags.BuildDebugArguments(port);
        try
        {
            Directory.CreateDirectory(AgentsDirectory);
            // Replace rather than merge: writing the file fresh each time keeps
            // the agent idempotent and free of stale ports.
            File.WriteAllText(path, BuildPlist(LabelFor(executablePath), executablePath, arguments));
            RunLaunchctl($"unload \"{path}\"");
            RunLaunchctl($"load -w \"{path}\"");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "launch-agent-write-failed",
                ("msg", SafeLogger.Redact(ex.Message)));
            return new PersistentLaunchResult { Success = false, Detail = "write-failed", Port = port };
        }

        _logger.Log(LogLevel.Information, LogCategories.Relaunch, "launch-agent-installed", ("port", port));
        return new PersistentLaunchResult
        {
            Success = true,
            Port = port,
            Updated =
            [
                new LaunchShortcut
                {
                    Path = path,
                    Location = "Login item",
                    Arguments = string.Join(' ', arguments),
                    HasDebugArguments = true,
                }
            ],
        };
    }

    public PersistentLaunchResult Remove(string executablePath)
    {
        var path = PlistPathFor(executablePath);
        if (!File.Exists(path))
            return new PersistentLaunchResult { Success = true, Detail = "not-configured" };
        try
        {
            RunLaunchctl($"unload \"{path}\"");
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "launch-agent-remove-failed",
                ("msg", SafeLogger.Redact(ex.Message)));
            return new PersistentLaunchResult { Success = false, Detail = "remove-failed" };
        }
        _logger.Log(LogLevel.Information, LogCategories.Relaunch, "launch-agent-removed");
        return new PersistentLaunchResult { Success = true };
    }

    /// <summary>
    /// Builds the agent plist. Every value is XML-escaped: an application path
    /// can legitimately contain characters that would otherwise close a tag and
    /// corrupt the file launchd parses.
    /// </summary>
    internal static string BuildPlist(string label, string executablePath, IReadOnlyList<string> arguments)
    {
        var entries = new List<string> { executablePath };
        entries.AddRange(arguments);
        var argXml = string.Join(
            Environment.NewLine,
            entries.Select(a => $"        <string>{SecurityElement.Escape(a)}</string>"));

        return
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{SecurityElement.Escape(label)}</string>
            <key>ProgramArguments</key>
            <array>
        {argXml}
            </array>
            <key>RunAtLoad</key>
            <true/>
        </dict>
        </plist>
        """;
    }

    /// <summary>Reads the ProgramArguments array back out of an agent plist.</summary>
    internal static IReadOnlyList<string> ParseProgramArguments(string plistXml)
    {
        var array = Regex.Match(plistXml,
            @"<key>ProgramArguments</key>\s*<array>(.*?)</array>",
            RegexOptions.Singleline);
        if (!array.Success) return [];
        return Regex.Matches(array.Groups[1].Value, @"<string>(.*?)</string>", RegexOptions.Singleline)
            .Select(m => Unescape(m.Groups[1].Value))
            .ToList();
    }

    private static string Unescape(string value) => value
        .Replace("&quot;", "\"", StringComparison.Ordinal)
        .Replace("&apos;", "'", StringComparison.Ordinal)
        .Replace("&lt;", "<", StringComparison.Ordinal)
        .Replace("&gt;", ">", StringComparison.Ordinal)
        .Replace("&amp;", "&", StringComparison.Ordinal);

    private void RunLaunchctl(string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("launchctl", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            proc?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            // launchd refusing an unload of an agent that was never loaded is
            // expected and must not fail the operation.
            _logger.Log(LogLevel.Debug, LogCategories.Relaunch, "launchctl-failed",
                ("msg", SafeLogger.Redact(ex.Message)));
        }
    }
}
