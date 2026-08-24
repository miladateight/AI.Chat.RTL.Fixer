// rtlfixer.rules.js — CANONICAL rule engine for AI RTL Fixer.
//
// This single file is the ONLY source of classification logic:
//   1. It is embedded into AI.ChatRTLFixer.Rules as a resource.
//   2. It is injected verbatim into the target chat page at runtime (ScriptBuilder).
//   3. It is executed by Jint inside unit tests (ReferenceEvaluator).
// C# never re-implements classification, so tests and runtime cannot diverge.
//
// It depends on rule-engine.shared.json for ranges/thresholds. When run in a
// browser, the bootstrap passes the parsed JSON in as RtlFixerRules.config.
// When run under Jint in tests, the ReferenceEvaluator sets RtlFixerRules.config.
//
// The public API is RtlFixerRules.classify(text) -> {
//   direction: "rtl"|"ltr", protected: bool, align: "start"|"left",
//   tokens: string[], rtlRatio: number, length: number
// }
(function () {
  "use strict";

  var RtlFixerRules = {};
  if (typeof module !== "undefined" && module.exports) { module.exports = RtlFixerRules; }
  if (typeof window !== "undefined") { window.RtlFixerRules = RtlFixerRules; }

  // --- Config (filled from rule-engine.shared.json) -------------------------
  var CFG = {
    rtlRanges: [
      [0x0590, 0x05ff], [0xfb1d, 0xfb4f],   // Hebrew
      [0x0600, 0x06ff], [0x0750, 0x077f], [0x08a0, 0x08ff],
      [0xfb50, 0xfdff], [0xfe70, 0xfeff]     // Arabic
    ],
    rtlRatio: 0.30,
    technicalRatio: 0.60,
    // Consecutive RTL words that make a block count as RTL prose regardless of
    // how much Latin text surrounds them. Three is the measured break-even: one
    // or two RTL words in a row are a quoted term or a proper noun inside an
    // English sentence, while three are a clause somebody wrote in Persian.
    rtlRunWords: 3,
    // Fallback weight for RTL words that never appear consecutively.
    scatteredRtlRatio: 0.15,
    // Share of lines that must look like source code before an unfenced block
    // is treated as code.
    codeLineRatio: 0.60
  };

  RtlFixerRules.setConfig = function (json) {
    if (json && json.rtlRanges) {
      CFG.rtlRanges = json.rtlRanges.map(function (r) {
        return [parseInt(r.from, 16), parseInt(r.to, 16)];
      });
    }
    if (json && json.thresholds) {
      var t = json.thresholds;
      if (typeof t.rtlRatio === "number") CFG.rtlRatio = t.rtlRatio;
      if (typeof t.technicalRatio === "number") CFG.technicalRatio = t.technicalRatio;
      if (typeof t.rtlRunWords === "number") CFG.rtlRunWords = t.rtlRunWords;
      if (typeof t.scatteredRtlRatio === "number") CFG.scatteredRtlRatio = t.scatteredRtlRatio;
      if (typeof t.codeLineRatio === "number") CFG.codeLineRatio = t.codeLineRatio;
    }
  };

  // --- RTL detection --------------------------------------------------------
  function isRtlChar(c) {
    var code = c.charCodeAt(0);
    for (var i = 0; i < CFG.rtlRanges.length; i++) {
      if (code >= CFG.rtlRanges[i][0] && code <= CFG.rtlRanges[i][1]) return true;
    }
    return false;
  }

  function rtlRatio(text) {
    var alpha = 0, rtl = 0;
    for (var i = 0; i < text.length; i++) {
      var ch = text[i];
      if (/[A-Za-z\u00C0-\u024F]/.test(ch) || /[\u0600-\u06FF\u0590-\u05FF]/.test(ch)) {
        alpha++;
        if (isRtlChar(ch)) rtl++;
      }
    }
    return alpha === 0 ? 0 : rtl / alpha;
  }
  RtlFixerRules.rtlRatio = rtlRatio;
  RtlFixerRules.isRtlChar = isRtlChar;

  // First strong directional character, mirroring HTML dir="auto" / the Unicode
  // bidi P2/P3 rule: scan until the first strongly-directional letter and let it
  // set the base direction. Digits, punctuation and whitespace are skipped. This
  // is what makes a Persian-first heading like "گزارش نهایی — App 1.2.0" read RTL
  // even when Latin product names drop its RTL ratio below the threshold.
  function firstStrongDir(text) {
    if (!text) return null;
    for (var i = 0; i < text.length; i++) {
      var ch = text[i];
      if (isRtlChar(ch)) return "rtl";
      if (/[A-Za-z]/.test(ch)) return "ltr";
    }
    return null;
  }
  RtlFixerRules.firstStrongDir = firstStrongDir;

  // Split a block into words and tag each one by script, tracking the longest
  // CONSECUTIVE run of RTL words. The run length is the useful signal: two or
  // more RTL words in a row is a clause someone actually wrote in that
  // language, whereas a lone RTL word inside English prose is a quoted term.
  function rtlWordStats(text) {
    var words = text.match(/[A-Za-z\u00C0-\u024F]+|[\u0590-\u05FF\u0600-\u08FF\uFB1D-\uFEFF]+/g) || [];
    var rtl = 0, latin = 0, run = 0, longestRun = 0;
    for (var i = 0; i < words.length; i++) {
      if (isRtlChar(words[i][0])) {
        rtl++; run++;
        if (run > longestRun) longestRun = run;
      } else {
        latin++; run = 0;
      }
    }
    return { rtl: rtl, latin: latin, longestRtlRun: longestRun, total: words.length };
  }
  RtlFixerRules.rtlWordStats = rtlWordStats;

  // Does this block contain real RTL prose, no matter what precedes it?
  //
  // A chat answer is routinely Persian prose carrying a lot of Latin: product
  // names, model names, API identifiers, English technical terms. Counting
  // characters, or requiring RTL words to OUTNUMBER Latin words, misreads all of
  // those as English and leaves the sentence left-aligned \u2014 the single most
  // common complaint. So the test is not "which script wins" but "is there a
  // stretch of RTL words here": scan the whole block to its end and accept it as
  // RTL as soon as MIN consecutive RTL words appear anywhere in it.
  //
  // A single isolated RTL word does NOT qualify, which is what keeps English
  // prose quoting one foreign term ("the Persian word for hello is \u0633\u0644\u0627\u0645")
  // left-aligned. Technical whole-block detection still runs before this rule,
  // so code, config and traces never reach it.
  function hasRtlProse(text) {
    var s = rtlWordStats(text);
    if (s.rtl === 0) return false;
    if (s.longestRtlRun >= CFG.rtlRunWords) return true;
    // RTL words scattered as single words with no run: accept only when they
    // still carry real weight in the block.
    return s.rtl >= CFG.rtlRunWords && rtlRatio(text) >= CFG.scatteredRtlRatio;
  }
  RtlFixerRules.hasRtlProse = hasRtlProse;

  // --- Technical text patterns (ES2018-safe: no lookbehind, no named groups)
  var RE = {
    codeFence: /(^|\n)\s*```/,
    inlineCode: /`[^`\n]+`/,
    winPath: /[A-Za-z]:[\\\/][^\s"'<>|]*/g,
    linuxPath: /(^|\s)(\/[A-Za-z0-9._\-\/]+)/g,
    url: /\bhttps?:\/\/[^\s"'<>]+/gi,
    command: /(^|\n)\s*(?:\$|>|npm |yarn |pnpm |dotnet |git |python |pip |node |cargo |rustc |docker |kubectl |cd |ls |mkdir |rm |cp |mv |chmod )/,
    json: /(^|\n)\s*[\{\[][\s\S]*[\}\]]\s*$/,
    yaml: /(^|\n)\s*[A-Za-z0-9_.\-]+\s*:\s/,
    xml: /(^|\n)\s*<\?xml|<\/?[A-Za-z][\s\S]*>/,
    toml: /(^|\n)\s*\[[A-Za-z0-9_.\-]+\]/,
    ini: /(^|\n)\s*[A-Za-z0-9_.\-]+\s*=\s*/,
    env: /(^|\n)\s*[A-Z_][A-Z0-9_]*=\S*/,
    stackTrace: /(^|\n)\s*at\s+[A-Za-z0-9_$.\/]+(\s+\([^)]*\))?:\d+:\d+/,
    diff: /(^|\n)\s*(@@ -\d+,\d+ \+\d+,\d+ @@|\+{3} |\-{3} )/,
    log: /(^|\n)\s*(\d{4}-\d{2}-\d{2}|\[\d{2}:\d{2}:\d{2}\]|INFO|WARN|ERROR|DEBUG|TRACE)\b/i,
    branchName: /\b(git\s+(checkout|branch|switch)\s+|origin\/|feature\/|fix\/|release\/|hotfix\/)[A-Za-z0-9._\-\/]+/i,
    packageName: /\b(@[a-z0-9_.\-]+\/[a-z0-9_.\-]+|[a-z0-9_.\-]+@[0-9]+\.[0-9]+\.[0-9]+(?:-[a-z0-9.]+)?)\b/i,
    versionNumber: /\bv?\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?\b/
  };

  function detectTokens(text) {
    var tokens = [];
    if (RE.codeFence.test(text)) tokens.push("codeBlock");
    if (RE.inlineCode.test(text)) tokens.push("inlineCode");
    if (RE.winPath.test(text)) tokens.push("winPath");
    if (RE.linuxPath.test(text)) tokens.push("linuxPath");
    if (RE.url.test(text)) tokens.push("url");
    if (RE.command.test(text)) tokens.push("command");
    if (RE.json.test(text)) tokens.push("json");
    if (RE.yaml.test(text)) tokens.push("yaml");
    if (RE.xml.test(text)) tokens.push("xml");
    if (RE.toml.test(text)) tokens.push("toml");
    if (RE.ini.test(text)) tokens.push("ini");
    if (RE.env.test(text)) tokens.push("env");
    if (RE.stackTrace.test(text)) tokens.push("stackTrace");
    if (RE.diff.test(text)) tokens.push("diff");
    if (RE.log.test(text)) tokens.push("log");
    if (RE.branchName.test(text)) tokens.push("branchName");
    if (RE.packageName.test(text)) tokens.push("packageName");
    if (RE.versionNumber.test(text)) tokens.push("versionNumber");
    // reset lastIndex on global regexes
    RE.winPath.lastIndex = 0; RE.linuxPath.lastIndex = 0; RE.url.lastIndex = 0;
    return tokens;
  }
  RtlFixerRules.detectTokens = detectTokens;

  // Does a single line look like source code rather than a sentence?
  // Used only for MULTI-line blocks, so a prose line that happens to contain a
  // word like "return" can never trip it on its own.
  function looksLikeSourceLine(line) {
    return /[;{}]\s*$/.test(line) ||                                  // ends in ; { }
      /^\s*(\/\/|\/\*|\*\/|\*\s|#\s*(?:include|define|!)|<!--)/.test(line) ||  // comment openers
      /=>|::|->|\+\+|--;|\breturn\s|\bawait\s/.test(line) ||          // operators/keywords
      /^\s*(function|class|def|import|export|const|let|var|public|private|protected|static|void|async|if|for|while|switch|try|catch|elif|else|end|fn|struct|impl|package|namespace|using)\b[\s({:]/.test(line);
  }

  // An unfenced code block: several lines that are mostly source code. Chat apps
  // usually wrap code in <pre>/<code>, which the runtime protects by selector,
  // but unverified profiles fall back to a wider scan where no selector matches.
  // Without this, a code snippet carrying Persian comments could be flipped.
  function isUnfencedSourceCode(lines) {
    if (lines.length < 2) return false;
    var codeLines = 0;
    for (var i = 0; i < lines.length; i++) {
      if (looksLikeSourceLine(lines[i])) codeLines++;
    }
    return codeLines / lines.length >= CFG.codeLineRatio;
  }

  // A block is "technical" if it is a fenced code block, or if it looks like a
  // whole structured config/trace/diff/log block rather than prose.
  function isTechnicalBlock(text, tokens) {
    // Fenced code block -> always technical.
    if (RE.codeFence.test(text)) return true;

    // Unfenced multi-line source code -> technical, even when it carries RTL
    // comments. Checked before the token gate below because source code does
    // not reliably produce any of the config/trace/log tokens.
    var allLines = text.split(/\n/).filter(function (l) { return l.trim().length > 0; });
    if (isUnfencedSourceCode(allLines)) return true;

    var technicalTokens = ["json", "yaml", "xml", "toml", "ini", "env", "stackTrace", "diff", "log", "command"];
    var hits = tokens.filter(function (t) { return technicalTokens.indexOf(t) >= 0; }).length;
    if (hits === 0) return false;

    var lines = text.split(/\n/).filter(function (l) { return l.trim().length > 0; });
    if (lines.length === 0) return false;

    // JSON/XML: the block is bracketed or starts with an xml declaration / tag.
    var trimmed = text.trim();
    var looksJson = /^\{[\s\S]*\}$|^\[[\s\S]*\]$/.test(trimmed) || /^\{\s*$/.test(lines[0]);
    if (tokens.indexOf("json") >= 0 && looksJson) return true;
    if (tokens.indexOf("xml") >= 0 && /^\s*<\?xml|<\w+.*>/.test(trimmed)) return true;
    // Diff: requires a hunk header to avoid false-positing on markdown bullets.
    if (tokens.indexOf("diff") >= 0 && /@@ -\d+,\d+ \+\d+,\d+ @@/.test(text)) return true;

    // A rendered list item such as "OpenAI: پاسخ فارسی" loses its Markdown
    // bullet before textContent reaches us and resembles one line of YAML.
    // Let clear RTL prose continue to the language classifier; retain protection
    // for unmistakable one-line env/log/stack-trace records.
    var unmistakableSingleLine = ["env", "stackTrace", "log"];
    var hasUnmistakableSingleLineToken = tokens.some(function (t) {
      return unmistakableSingleLine.indexOf(t) >= 0;
    });
    var looksLikeRtlProse = firstStrongDir(text) === "rtl" ||
      rtlRatio(text) >= CFG.rtlRatio || hasRtlProse(text);
    if (lines.length === 1 && looksLikeRtlProse && !hasUnmistakableSingleLineToken) return false;

    // Key:value / structured / trace / diff / log line detector.
    var structuredLines = 0;
    for (var i = 0; i < lines.length; i++) {
      var l = lines[i];
      if (/^\s*[\{\}\[\]]/.test(l)) { structuredLines++; continue; }
      // key: value OR "key": value OR key = value (whitespace after separator optional)
      if (/^\s*("?)[A-Za-z0-9_.\-]+\1\s*[:=]\s?/.test(l)) { structuredLines++; continue; }
      if (/^\s*(at\s+|@@ |<\?xml|<\/?)/.test(l)) { structuredLines++; continue; }
      if (/^\s*(\+{3}|\-{3}) /.test(l)) { structuredLines++; continue; }
      if (/^\s*(\d{4}-\d{2}-\d{2}|\[\d{2}:\d{2}:\d{2}\]|INFO|WARN|ERROR|DEBUG)\b/i.test(l)) { structuredLines++; continue; }
      // toml section header
      if (/^\s*\[[A-Za-z0-9_.\-]+\]\s*$/.test(l)) { structuredLines++; continue; }
      // shell prompt / package-manager invocation
      if (RE.command.test("\n" + l)) { structuredLines++; continue; }
    }
    var structuredRatio = structuredLines / lines.length;
    return structuredRatio >= CFG.technicalRatio;
  }

  // --- Classification -------------------------------------------------------
  function classify(text) {
    if (text == null) text = "";
    var len = text.length;
    var tokens = detectTokens(text);
    var ratio = rtlRatio(text);

    // Technical whole-block -> Protected, LTR, never flipped, never markers.
    if (isTechnicalBlock(text, tokens)) {
      return { direction: "ltr", protected: true, align: "left", tokens: tokens, rtlRatio: ratio, length: len };
    }

    // A block is RTL if it is RTL-heavy (ratio over threshold), OR it simply
    // begins with an RTL letter (first-strong, like dir="auto"), OR it contains
    // a run of RTL words anywhere in it. The last clause is what makes the
    // whole block get read to its end instead of letting a Latin opening word
    // decide alignment for a sentence that is really Persian.
    if (ratio < CFG.rtlRatio && firstStrongDir(text) !== "rtl" &&
        !hasRtlProse(text)) {
      return { direction: "ltr", protected: false, align: "left", tokens: tokens, rtlRatio: ratio, length: len };
    }

    // RTL content, not a technical block -> RTL, right-aligned.
    return { direction: "rtl", protected: false, align: "start", tokens: tokens, rtlRatio: ratio, length: len };
  }
  RtlFixerRules.classify = classify;

  // --- Node classification (for the runtime observer) -----------------------
  // Returns the same shape as classify, but accounts for a node being inside a
  // protected selector (passed in by the bootstrap as a boolean).
  function classifyNode(text, isInsideProtected) {
    if (isInsideProtected) {
      var t = detectTokens(text);
      return { direction: "ltr", protected: true, align: "left", tokens: t, rtlRatio: rtlRatio(text), length: text ? text.length : 0 };
    }
    return classify(text);
  }
  RtlFixerRules.classifyNode = classifyNode;

  // --- Clipboard helpers ----------------------------------------------------
  // Wrap embedded LTR runs inside RTL natural-language text so logical order is
  // preserved. Markers are only applied to natural-language RTL text, never to
  // code/path/url/command/config.
  var RLM = "\u200F"; // RIGHT-TO-LEFT MARK
  var LRM = "\u200E"; // LEFT-TO-RIGHT MARK

  // Returns plain text with optional invisible bidi marks around RTL runs.
  function buildPlainText(text, mode) {
    if (mode === "original" || mode === "Original") return text;
    if (mode === "rtlReadableNoMarkers" || mode === "RtlReadableNoMarkers") {
      // Logical order is already correct in the DOM text; no markers needed.
      return text;
    }
    // rtlReadable: add RLM around RTL natural-language runs.
    // We do NOT touch text inside code fences/paths/urls.
    return addRtlMarkers(text);
  }
  RtlFixerRules.buildPlainText = buildPlainText;

  function addRtlMarkers(text) {
    // Conservative: prepend RLM if the text starts with RTL, append RLM if it
    // ends with RTL. This avoids spraying markers into embedded code tokens.
    var out = text;
    if (out.length > 0 && isRtlChar(out[0])) out = RLM + out;
    if (out.length > 0 && isRtlChar(out[out.length - 1])) out = out + RLM;
    return out;
  }

  // Returns safe HTML for the clipboard with dir/bdi/isolate spans.
  function buildHtml(text, mode, direction) {
    var dir = direction === "rtl" ? "rtl" : "ltr";
    var escaped = htmlEscape(text);
    if (mode === "original" || mode === "Original") {
      return "<span>" + escaped + "</span>";
    }
    return '<span dir="' + dir + '" style="unicode-bidi: isolate">' + escaped + "</span>";
  }
  RtlFixerRules.buildHtml = buildHtml;

  function htmlEscape(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }
  RtlFixerRules.htmlEscape = htmlEscape;

  // --- Restore helpers ------------------------------------------------------
  // The bootstrap records original attribute values and re-applies them.
  // This module exposes an apply/restore pair used by the observer bootstrap.
  RtlFixerRules.applyToNode = function (el, decision) {
    if (!el || el.getAttribute && el.getAttribute("data-rtlfixer") === "applied") return false;
    if (el.setAttribute) {
      el.setAttribute("data-rtlfixer", "applied");
      el.setAttribute("dir", decision.direction);
      el.style.textAlign = decision.align;
      return true;
    }
    return false;
  };

  RtlFixerRules.restoreNode = function (el) {
    if (!el || !el.removeAttribute || el.getAttribute("data-rtlfixer") !== "applied") return false;
    el.removeAttribute("data-rtlfixer");
    el.removeAttribute("dir");
    if (el.style) el.style.textAlign = "";
    return true;
  };

  return RtlFixerRules;
})();
