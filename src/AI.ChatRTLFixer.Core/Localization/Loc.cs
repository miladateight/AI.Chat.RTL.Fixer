using System.Reflection;
using System.Text.Json;

namespace AI.ChatRTLFixer.Core.Localization;

/// <summary>
/// Interface strings, looked up by key.
///
/// <para>
/// Deliberately not <c>ResourceManager</c>/satellite assemblies: the tray ships
/// as one self-contained executable, and satellite assemblies would add a
/// per-language folder next to it that the installer would have to carry and
/// that is easy to lose. One embedded JSON file per language keeps the whole
/// interface inside the single binary.
/// </para>
///
/// <para>
/// A missing key falls back to English rather than throwing or showing the raw
/// key: a translation that has not caught up yet should leave the app usable,
/// not blank out a button.
/// </para>
/// </summary>
public static class Loc
{
    private const string ResourcePrefix = "AI.ChatRTLFixer.Core.Localization.Strings.";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static IReadOnlyDictionary<string, string> _current = Load(UiLanguages.DefaultCode);
    private static IReadOnlyDictionary<string, string> _fallback = Load("en");

    /// <summary>The language currently in use.</summary>
    public static UiLanguage Current { get; private set; } = UiLanguages.Default;

    /// <summary>True when the interface itself should be laid out right-to-left.</summary>
    public static bool IsRtl => Current.IsRtl;

    /// <summary>
    /// Switches the interface language. An unknown or empty code selects the
    /// default rather than leaving the app half-translated.
    /// </summary>
    public static void SetLanguage(string? code)
    {
        var language = UiLanguages.Get(code);
        lock (Gate)
        {
            Current = language;
            _current = Load(language.Code);
            _fallback = Load("en");
        }
    }

    /// <summary>Looks up <paramref name="key"/> in the current language.</summary>
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        IReadOnlyDictionary<string, string> current, fallback;
        lock (Gate) { current = _current; fallback = _fallback; }

        if (current.TryGetValue(key, out var value)) return value;
        if (fallback.TryGetValue(key, out var english)) return english;
        return key;
    }

    /// <summary>
    /// Looks up <paramref name="key"/> and fills its <c>{0}</c>-style placeholders.
    /// Formatting never throws: a translation with the wrong placeholder count
    /// returns the unformatted string instead of crashing the window it is on.
    /// </summary>
    public static string T(string key, params object?[] args)
    {
        var template = T(key);
        if (args.Length == 0) return template;
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    private static IReadOnlyDictionary<string, string> Load(string code)
    {
        if (Cache.TryGetValue(code, out var cached)) return cached;

        var map = ReadEmbedded(code) ?? new Dictionary<string, string>();
        Cache[code] = map;
        return map;
    }

    private static IReadOnlyDictionary<string, string>? ReadEmbedded(string code)
    {
        var name = ResourcePrefix + code + ".json";
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return parsed is null ? null : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every key defined for a language. Used by the tests that check coverage.</summary>
    public static IReadOnlyCollection<string> KeysFor(string code) => Load(code).Keys.ToList();
}
