using System.Globalization;
using System.Text;
using System.Text.Json;
using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.Diagnostics;

/// <summary>
/// Privacy-safe logger. Never writes chat text or clipboard content. By default
/// every potentially-private string is redacted to a short metadata summary.
/// Only when <paramref name="developerMode"/> is true are short (truncated)
/// text samples allowed, and even then only via <see cref="LogTextSample"/>.
/// </summary>
public sealed class SafeLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly LogLevel _level;
    private readonly bool _developerMode;
    private readonly long _maxFileBytes;
    private readonly int _maxRotated;
    private StreamWriter? _writer;
    private bool _disposed;

    public SafeLogger(string path, LogLevel level, bool developerMode, long maxFileBytes = 1_000_000, int maxRotated = 5)
    {
        _path = path;
        _level = level;
        _developerMode = developerMode;
        _maxFileBytes = maxFileBytes;
        _maxRotated = maxRotated;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Open();
    }

    public void Log(LogLevel severity, string category, string message, params (string Key, object? Value)[] fields)
    {
        if (severity < _level) return;
        var line = Format(severity, category, message, fields);
        lock (_gate)
        {
            if (_writer is null) return;
            _writer.WriteLine(line);
            _writer.Flush();
            MaybeRotate();
        }
    }

    /// <summary>
    /// Logs a short text sample from the DOM. ONLY allowed in developer mode.
    /// The sample is truncated and quoted; never logs full message bodies.
    /// </summary>
    public void LogTextSample(string category, string text, int maxChars = 40)
    {
        if (!_developerMode) return;
        if (string.IsNullOrEmpty(text)) return;
        var sample = text.Length <= maxChars ? text : text[..maxChars] + "…";
        Log(LogLevel.Debug, category, "text-sample", ("sample", sample), ("len", text.Length));
    }

    /// <summary>
    /// Redacts arbitrary text that might be chat content into a safe metadata
    /// summary. Use this whenever a DOM-derived string would otherwise reach a log.
    /// </summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "len=0";
        var alpha = 0;
        var rtl = 0;
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                alpha++;
                if (IsRtlChar(ch)) rtl++;
            }
        }
        var ratio = alpha == 0 ? 0 : (double)rtl / alpha;
        return string.Format(CultureInfo.InvariantCulture, "len={0},rtlRatio={1:F2}", text.Length, ratio);
    }

    private static string Format(LogLevel severity, string category, string message, (string Key, object? Value)[] fields)
    {
        var sb = new StringBuilder();
        sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        sb.Append('\t').Append(severity.ToString().ToUpperInvariant());
        sb.Append('\t').Append(category);
        sb.Append('\t').Append(message);
        if (fields.Length > 0)
        {
            sb.Append('\t');
            sb.Append(JsonSerializer.Serialize(fields.ToDictionary(f => f.Key, f => f.Value)));
        }
        return sb.ToString();
    }

    private static bool IsRtlChar(char ch)
    {
        var c = (int)ch;
        return (c >= 0x0590 && c <= 0x05FF) || (c >= 0xFB1D && c <= 0xFB4F) ||
               (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) ||
               (c >= 0x08A0 && c <= 0x08FF) || (c >= 0xFB50 && c <= 0xFDFF) ||
               (c >= 0xFE70 && c <= 0xFEFF);
    }

    private void Open()
    {
        var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false));
    }

    private void MaybeRotate()
    {
        try
        {
            if (_writer is null || _writer.BaseStream is not FileStream fs) return;
            if (fs.Length < _maxFileBytes) return;
        }
        catch { return; }

        _writer!.Dispose();
        _writer = null;

        for (var i = _maxRotated - 1; i >= 1; i--)
        {
            var from = _path + "." + i;
            var to = _path + "." + (i + 1);
            if (File.Exists(from)) File.Move(from, to, overwrite: true);
        }
        try { File.Move(_path, _path + ".1", overwrite: true); } catch { }

        Open();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}