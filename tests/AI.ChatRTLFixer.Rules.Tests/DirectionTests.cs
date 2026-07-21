using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Rules;

namespace AI.ChatRTLFixer.Rules.Tests;

public class DirectionTests
{
    private readonly ReferenceEvaluator _eval = new();

    // (name, text, expectedDirection, expectedProtected)
    public static IEnumerable<object[]> Cases => new[]
    {
        // RTL-only blocks -> RTL, not protected.
        new object[] { "persian-only", "سلام دنیا، این یک پیام فارسی است.", BlockDirection.Rtl, false },
        new object[] { "arabic-only", "مرحبا بالعالم، هذه رسالة باللغة العربية.", BlockDirection.Rtl, false },
        new object[] { "hebrew-only", "שלום עולם, זהו מסר בעברית.", BlockDirection.Rtl, false },
        new object[] { "urdu-only", "ہیلو دنیا، یہ ایک اردو پیام ہے۔", BlockDirection.Rtl, false },

        // English-only -> LTR, not protected.
        new object[] { "english-only", "Hello world, this is an English message.", BlockDirection.Ltr, false },

        // Mixed Persian + English (RTL present) -> RTL.
        new object[] { "mixed-fa-en", "از این ابزار برای کدنویسی استفاده کن use it wisely.", BlockDirection.Rtl, false },
        new object[] { "persian-with-numbers", "نسخه 3 از فایل در پوشه ۲ وجود دارد.", BlockDirection.Rtl, false },

        // Persian with embedded technical tokens -> still RTL block (tokens detected, not whole-block technical).
        new object[] { "persian-with-winpath", "فایل در مسیر C:\\Users\\Milad\\Project قرار دارد.", BlockDirection.Rtl, false },
        new object[] { "persian-with-linuxpath", "لاگ‌ها در /var/log/app.log ذخیره می‌شوند.", BlockDirection.Rtl, false },
        new object[] { "persian-with-url", "برای اطلاعات بیشتر به https://example.com/fa مراجعه کن.", BlockDirection.Rtl, false },
        new object[] { "persian-with-inline-code", "برای اجرا از `dotnet build` استفاده کن.", BlockDirection.Rtl, false },
        new object[] { "persian-with-command", "برای نصب اجرا کن: npm install", BlockDirection.Rtl, false },

        // Persian-first heading whose Latin product name pulls the RTL ratio just
        // under the threshold -> still RTL via first-strong (dir=auto behavior).
        new object[] { "persian-first-latin-heavy", "گزارش نهایی — PdfSanitizer 0.4.0 (Hardening & UX polish)", BlockDirection.Rtl, false },
        // Latin/path-dominant line (RTL ratio well under threshold) with a short
        // trailing Persian note -> LTR via first-strong + low ratio.
        new object[] { "latin-first-path", "src/pdfsanitizer/app/widgets/ocr_panel.py — OCR mismatch را درست کرد", BlockDirection.Ltr, false },

        // A long Latin product/model name must not make a Persian sentence LTR.
        // The first word is a label; the remaining natural-language words are RTL.
        new object[] { "latin-label-then-persian-prose", "OpenAIEnterpriseWorkspaceExperimentalPreview \u0627\u06CC\u0646 \u06CC\u06A9 \u062C\u0645\u0644\u0647 \u0641\u0627\u0631\u0633\u06CC \u06A9\u0627\u0645\u0644 \u0627\u0633\u062A.", BlockDirection.Rtl, false },

        // Markdown Persian -> RTL (content-driven).
        new object[] { "md-heading", "# عنوان فارسی\nاین یک بخش فارسی است.", BlockDirection.Rtl, false },
        new object[] { "md-bullet", "- مورد اول فارسی\n- مورد دوم فارسی\n- مورد سوم", BlockDirection.Rtl, false },
        new object[] { "md-numbered", "1. قدم اول فارسی\n2. قدم دوم فارسی", BlockDirection.Rtl, false },
        new object[] { "md-blockquote", "> این یک نقل‌قول فارسی است.", BlockDirection.Rtl, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_MatchesExpected(string _1, string text, BlockDirection expectedDirection, bool expectedProtected)
    {
        _ = _1;
        var c = _eval.Classify(text);
        Assert.Equal(expectedDirection, c.Direction);
        Assert.Equal(expectedProtected, c.Protected);
    }
}
