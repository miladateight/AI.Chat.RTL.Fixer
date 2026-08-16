// rtlfixer.rules.js — CANONICAL rule engine for AI Chat RTL Fixer.
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
    technicalRatio: 0.60
  };

  RtlFixerRules.setConfig = function (json) {
    if (json && json.rtlRanges) {
      CFG.rtlRanges = json.rtlRanges.map(function (r) {
        return [parseInt(r.from, 16), parseInt(r.to, 16)];
      });
    }
    if (json && json.thresholds) {
      if (typeof json.thresholds.rtlRatio === "number") CFG.rtlRatio = json.thresholds.rtlRatio;
      if (typeof json.thresholds.technicalRatio === "number") CFG.technicalRatio = json.thresholds.technicalRatio;
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

  // A chat message may start with one or more Latin product/model names while
  // the actual sentence that follows is Persian/Arabic/Hebrew. Long Latin
  // identifiers can pull the character ratio below the threshold even though
  // the prose is clearly RTL. Prefer RTL only when RTL words outnumber all
  // Latin words and there are at least two of them. This keeps English prose
  // with a short RTL quote LTR; technical whole-block detection still runs
  // before this rule.
  function hasRtlProseAfterLeadingLatinText(text) {
    if (firstStrongDir(text) !== "ltr") return false;
    var words = text.match(/[A-Za-z\u00C0-\u024F]+|[\u0590-\u05FF\u0600-\u08FF\uFB1D-\uFEFF]+/g) || [];
    var latinWords = 0, rtlWords = 0;
    for (var i = 0; i < words.length; i++) {
      if (isRtlChar(words[i][0])) rtlWords++;
      else latinWords++;
    }
    return rtlWords >= 2 && rtlWords > latinWords;
  }

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

  // A block is "technical" if it is a fenced code block, or if it looks like a
  // whole structured config/trace/diff/log block rather than prose.
  function isTechnicalBlock(text, tokens) {
    // Fenced code block -> always technical.
    if (RE.codeFence.test(text)) return true;

    var technicalTokens = ["json", "yaml", "xml", "toml", "ini", "env", "stackTrace", "diff", "log"];
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
      rtlRatio(text) >= CFG.rtlRatio || hasRtlProseAfterLeadingLatinText(text);
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

    // A block is RTL if it is RTL-heavy (ratio over threshold) OR it simply
    // begins with an RTL letter (first-strong, like dir="auto"). The second
    // clause catches Persian-first prose whose Latin product names / paths pull
    // the ratio just under the threshold — the common miss on coding assistants.
    if (ratio < CFG.rtlRatio && firstStrongDir(text) !== "rtl" &&
        !hasRtlProseAfterLeadingLatinText(text)) {
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
