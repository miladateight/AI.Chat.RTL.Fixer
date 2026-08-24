using System.Text.RegularExpressions;
using AI.ChatRTLFixer.Core.Localization;

namespace AI.ChatRTLFixer.Core.Tests;

/// <summary>
/// Translations fail quietly: a missing key shows English inside an otherwise
/// translated window, and a placeholder typo shows a raw template to the user.
/// Neither throws, so nothing surfaces them except tests like these.
/// </summary>
public class LocalizationTests
{
    private static IReadOnlyCollection<string> EnglishKeys => Loc.KeysFor("en");

    [Fact]
    public void EveryShippedLanguageHasStrings()
    {
        foreach (var language in UiLanguages.All)
        {
            Assert.True(Loc.KeysFor(language.Code).Count > 0,
                $"{language.EnglishName} ({language.Code}) has no strings file");
        }
    }

    [Fact]
    public void EveryLanguageCoversEveryEnglishKey()
    {
        foreach (var language in UiLanguages.All)
        {
            var missing = EnglishKeys.Except(Loc.KeysFor(language.Code)).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0,
                $"{language.Code} is missing: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void NoLanguageDefinesKeysEnglishDoesNot()
    {
        // A key only present in one translation is dead weight — nothing reads
        // it — and usually means a rename that was applied unevenly.
        foreach (var language in UiLanguages.All)
        {
            var extra = Loc.KeysFor(language.Code).Except(EnglishKeys).OrderBy(k => k).ToList();
            Assert.True(extra.Count == 0,
                $"{language.Code} defines unknown keys: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void PlaceholdersMatchEnglishForEveryKey()
    {
        foreach (var language in UiLanguages.All.Where(l => l.Code != "en"))
        {
            Loc.SetLanguage("en");
            foreach (var key in EnglishKeys)
            {
                var expected = Placeholders(Loc.T(key));
                Loc.SetLanguage(language.Code);
                var actual = Placeholders(Loc.T(key));
                Loc.SetLanguage("en");

                Assert.True(expected.SetEquals(actual),
                    $"{language.Code}/{key}: expected placeholders [{string.Join(",", expected.Order())}] " +
                    $"but found [{string.Join(",", actual.Order())}]");
            }
        }
        Loc.SetLanguage(UiLanguages.DefaultCode);
    }

    [Fact]
    public void DefaultLanguageIsPersianAndRtl()
    {
        Loc.SetLanguage(null);
        Assert.Equal("fa", Loc.Current.Code);
        Assert.True(Loc.IsRtl);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownLanguageFallsBackToTheDefault(string? code)
    {
        Loc.SetLanguage(code);
        Assert.Equal(UiLanguages.DefaultCode, Loc.Current.Code);
    }

    [Fact]
    public void EnglishIsTheOnlyLeftToRightLanguage()
    {
        Assert.Equal(["en"], UiLanguages.All.Where(l => !l.IsRtl).Select(l => l.Code));
    }

    [Fact]
    public void EveryLanguageIsNamedInItsOwnScript()
    {
        // The picker shows native names only, so somebody who cannot read the
        // current interface language can still find their own.
        foreach (var language in UiLanguages.All)
            Assert.False(string.IsNullOrWhiteSpace(language.NativeName), language.Code);

        Assert.Equal("فارسی", UiLanguages.Get("fa").NativeName);
        Assert.Equal("עברית", UiLanguages.Get("he").NativeName);
        Assert.Equal("العربية", UiLanguages.Get("ar").NativeName);
        Assert.Equal("اردو", UiLanguages.Get("ur").NativeName);
    }

    [Fact]
    public void MissingKeyReturnsTheKeyRatherThanThrowing()
    {
        Loc.SetLanguage("fa");
        Assert.Equal("no.such.key", Loc.T("no.such.key"));
    }

    [Fact]
    public void FormattingWithTooFewArgumentsDoesNotThrow()
    {
        Loc.SetLanguage("fa");
        // Guards the window that would otherwise crash on a translation whose
        // placeholder count drifted from the call site.
        var value = Loc.T("relaunch.failed");
        Assert.False(string.IsNullOrEmpty(value));
    }

    [Fact]
    public void NoTranslationIsLeftAsTheEnglishSource()
    {
        // Catches a language file that was copied from en.json and never
        // translated. A handful of shared strings legitimately match.
        Loc.SetLanguage("en");
        var english = EnglishKeys.ToDictionary(k => k, Loc.T);

        foreach (var language in UiLanguages.All.Where(l => l.Code != "en"))
        {
            Loc.SetLanguage(language.Code);
            var identical = english.Count(pair => Loc.T(pair.Key) == pair.Value);
            Assert.True(identical < english.Count / 4,
                $"{language.Code} still matches English for {identical}/{english.Count} keys");
        }
        Loc.SetLanguage(UiLanguages.DefaultCode);
    }

    private static HashSet<string> Placeholders(string template)
        => Regex.Matches(template, @"\{(\d+)\}").Select(m => m.Groups[1].Value).ToHashSet();
}
