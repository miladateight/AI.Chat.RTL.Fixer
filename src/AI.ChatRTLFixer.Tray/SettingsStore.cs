using System.Text.Json;
using System.Text.Json.Serialization;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Tray;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> atomically to
/// %AppData%\AIChatRTLFixer\settings.json. Never stores chat text.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly SafeLogger _logger;
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(SafeLogger logger) => _logger = logger;

    public async Task<AppSettings> LoadAsync(CancellationToken ct)
    {
        AppPaths.EnsureDirectories();
        var path = AppPaths.SettingsPath;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            await using var fs = File.OpenRead(path);
            var s = await JsonSerializer.DeserializeAsync<AppSettings>(fs, _opts, ct);
            return Migrate(s ?? new AppSettings());
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Settings, "load-failed", ("msg", SafeLogger.Redact(ex.Message)));
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        AppPaths.EnsureDirectories();
        var path = AppPaths.SettingsPath;
        var tmp = path + ".tmp";
        try
        {
            await using (var fs = File.Create(tmp))
                await JsonSerializer.SerializeAsync(fs, settings, _opts, ct);
            File.Move(tmp, path, overwrite: true);
            _logger.Log(LogLevel.Information, LogCategories.Settings, "saved");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogCategories.Settings, "save-failed", ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private static AppSettings Migrate(AppSettings s)
    {
        if (s.SchemaVersion < AppSettings.CurrentSchemaVersion)
        {
            s.SchemaVersion = AppSettings.CurrentSchemaVersion;
        }
        s.RelaunchCooldownSeconds = Math.Clamp(s.RelaunchCooldownSeconds, 10, 3600);
        s.DiscoveryTimeoutSeconds = Math.Clamp(s.DiscoveryTimeoutSeconds, 2, 60);
        s.InitialScanDelayMs = Math.Clamp(s.InitialScanDelayMs, 0, 10000);
        s.ReconciliationIntervalSeconds = Math.Clamp(s.ReconciliationIntervalSeconds, 2, 5);
        return s;
    }
}
