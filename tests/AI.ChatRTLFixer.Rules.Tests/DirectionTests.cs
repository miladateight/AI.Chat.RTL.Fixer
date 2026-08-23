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
        // Path-dominant line whose sentence is nevertheless Persian: "… را درست
        // کرد" is a Persian verb phrase, so the line reads right-to-left even
        // though a file path opens it and the RTL character ratio stays low.
        new object[] { "latin-first-path", "src/pdfsanitizer/app/widgets/ocr_panel.py — OCR mismatch را درست کرد", BlockDirection.Rtl, false },

        // A long Latin product/model name must not make a Persian sentence LTR.
        // The leading words are labels; the remaining natural-language words are RTL.
        new object[] { "latin-label-then-persian-prose", "OpenAIEnterpriseWorkspaceExperimentalPreview \u0627\u06CC\u0646 \u06CC\u06A9 \u062C\u0645\u0644\u0647 \u0641\u0627\u0631\u0633\u06CC \u06A9\u0627\u0645\u0644 \u0627\u0633\u062A.", BlockDirection.Rtl, false },
        new object[] { "multiple-latin-labels-then-persian-prose", "OpenAIEnterpriseWorkspaceExperimentalPreview GPT-5: \u0627\u06CC\u0646 \u067E\u0627\u0633\u062E \u0641\u0627\u0631\u0633\u06CC \u06A9\u0627\u0645\u0644 \u0627\u0633\u062A.", BlockDirection.Rtl, false },
        new object[] { "latin-yaml-like-label-then-persian-prose", "OpenAIEnterpriseWorkspaceExperimentalPreview: \u0627\u06CC\u0646 \u0645\u0648\u0631\u062F \u0641\u0627\u0631\u0633\u06CC \u06A9\u0627\u0645\u0644 \u0627\u0633\u062A", BlockDirection.Rtl, false },
        new object[] { "english-prose-with-short-persian-quote", "This update fixes the issue called \u062E\u0637\u0627\u06CC \u0627\u062A\u0635\u0627\u0644", BlockDirection.Ltr, false },

        // A block is read to its END: English may open and even dominate the
        // sentence, but a run of Persian words anywhere in it decides direction.
        // These are the everyday coding-assistant answers that used to stay
        // left-aligned because Latin words simply outnumbered the Persian ones.
        new object[] { "english-lead-then-persian-clause", "You should call the initialize method before running the worker thread تا درست کار کند.", BlockDirection.Rtl, false },
        new object[] { "english-sentence-then-persian-tail", "Install the package and restart the application service now بعد امتحان کن.", BlockDirection.Rtl, false },
        new object[] { "long-model-name-then-persian", "OpenAI GPT-5 Turbo Preview Enterprise Workspace: این پاسخ فارسی است.", BlockDirection.Rtl, false },
        new object[] { "english-line-then-persian-lines", "Install it first.\nسپس تنظیمات را باز کن.\nو ذخیره کن.", BlockDirection.Rtl, false },
        // One quoted RTL word inside English prose is a term, not a clause.
        new object[] { "english-prose-with-one-persian-word", "The Persian word for hello is سلام.", BlockDirection.Ltr, false },

        // Source code keeps its direction even when its COMMENTS are Persian.
        // Reading the block to its end must never reach into code.
        new object[] { "js-code-with-persian-comment", "// این مقدار را تغییر بده\nconst maxRetries = 5;\nif (x > 3) { run(); }", BlockDirection.Ltr, true },
        new object[] { "python-code-with-persian-comment", "# مقدار پیش فرض را عوض کن\ndef run(x):\n    return x + 1", BlockDirection.Ltr, true },
        new object[] { "csharp-code-with-persian-comment", "// سرویس را ثبت کن\npublic void Configure() {\n    services.Add();\n}", BlockDirection.Ltr, true },
        new object[] { "shell-command-block", "$ git clone repo\n$ cd repo\n$ npm install", BlockDirection.Ltr, true },
        // …but a Persian sentence that merely mentions a code keyword is prose.
        new object[] { "persian-prose-with-code-keyword", "اگر مقدار درست بود return کن و بقیه را رها کن.", BlockDirection.Rtl, false },

        // Markdown Persian -> RTL (content-driven).
        new object[] { "md-heading", "# عنوان فارسی\nاین یک بخش فارسی است.", BlockDirection.Rtl, false },
        new object[] { "md-bullet", "- مورد اول فارسی\n- مورد دوم فارسی\n- مورد سوم", BlockDirection.Rtl, false },
        new object[] { "md-bullet-with-latin-labels", "- OpenAIEnterpriseWorkspaceExperimentalPreview: \u067E\u0627\u0633\u062E \u0641\u0627\u0631\u0633\u06CC\n- GPT-5: \u0645\u062A\u0646 \u0641\u0627\u0631\u0633\u06CC \u06A9\u0627\u0645\u0644", BlockDirection.Rtl, false },
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
