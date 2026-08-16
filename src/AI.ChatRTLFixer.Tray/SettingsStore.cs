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
    private readonly SemaphoreSlim _saveGate = new(1, 1);
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
            return (s ?? new AppSettings()).Normalize();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Settings, "load-failed", ("msg", SafeLogger.Redact(ex.Message)));
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        await _saveGate.WaitAsync(ct);
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
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            _saveGate.Release();
        }
    }

}
