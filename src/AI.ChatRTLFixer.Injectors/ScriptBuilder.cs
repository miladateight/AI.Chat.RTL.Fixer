using System.Text;
using System.Text.Json;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Rules;

namespace AI.ChatRTLFixer.Injectors;

/// <summary>
/// Builds the runtime script injected into a target chat page. It embeds the
/// canonical <c>rtlfixer.rules.js</c> verbatim (so runtime and tests share one
/// engine) and adds a MutationObserver bootstrap, a copy interceptor, and a
/// restore function. All modifications are tagged with data-rtlfixer attributes
/// and tracked for clean removal.
/// </summary>
public static class ScriptBuilder
{
    /// <summary>
    /// Builds the full runtime script for a profile and copy mode.
    /// </summary>
    public static string Build(AppProfile profile, CopyMode copyMode)
    {
        var s = profile.Selectors;
        var js = RuleResources.LoadRulesJs();
        // The shared JSON config (ranges/thresholds). The SAME object is fed to
        // the engine in tests (ReferenceEvaluator.setConfig). Embedding it here
        // keeps runtime and tests on one source of truth: if the JSON changes,
        // both sides pick it up instead of the runtime silently using the
        // hard-coded fallback inside the JS file.
        var sharedConfigJson = RuleResources.LoadSharedConfig().GetRawText();

        var sb = new StringBuilder();
        sb.Append("(function () {\n");
        sb.Append("  'use strict';\n");
        // Guard: never install twice on the same page.
        sb.Append("  if (window['" + Constants.ScriptInstalledFlag + "']) return;\n");
        sb.Append("  window['" + Constants.ScriptInstalledFlag + "'] = true;\n");

        // CommonJS shim so the canonical rules IIFE can assign module.exports.
        sb.Append("  var module = { exports: {} };\n");
        sb.Append("  var exports = module.exports;\n");
        sb.Append(js);
        sb.Append("\n  var Rules = module.exports;\n");
        // Feed the shared config into the engine so thresholds/ranges come from
        // rule-engine.shared.json, not the JS hard-coded defaults.
        sb.Append("  Rules.setConfig(" + sharedConfigJson + ");\n");

        // Selectors + copy mode config.
        sb.Append("  var CFG = {\n");
        sb.Append("    chatContainer: " + JsStr(s.ChatContainer) + ",\n");
        sb.Append("    composer: " + JsStr(s.Composer) + ",\n");
        sb.Append("    userMessage: " + JsStr(s.UserMessage) + ",\n");
        sb.Append("    assistantMessage: " + JsStr(s.AssistantMessage) + ",\n");
        sb.Append("    messageRoot: " + JsStr(s.MessageRoot) + ",\n");
        sb.Append("    codeBlock: " + JsStr(s.CodeBlock) + ",\n");
        sb.Append("    inlineCode: " + JsStr(s.InlineCode) + ",\n");
        sb.Append("    copyRoot: " + JsStr(s.CopyRoot) + ",\n");
        var prot = s.Protected.Where(p => !string.IsNullOrEmpty(p)).Select(JsStr);
        sb.Append("    protected: [" + string.Join(",", prot) + "],\n");
        sb.Append("    fontScope: " + JsStr(s.FontScope) + ",\n");
        sb.Append("    copyMode: " + JsStr(copyMode.ToString()) + "\n");
        sb.Append("  };\n");

        sb.Append(Body);
        sb.Append("})();\n");

        return sb.ToString();
    }

    private const string Body = @"
    // --- registry of touched nodes, keyed by node, value = original attrs ---
    var registry = (typeof WeakMap !== 'undefined') ? new WeakMap() : null;
    function markNode(el, decision, marker, directionOnly) {
      if (!el || !el.setAttribute) return;
      marker = marker || 'applied';
      var previous = registry ? registry.get(el) : el.__rtlfixerPrev;
      if (!previous) {
        var st = el.style || {};
        var prev = {
          dir: el.getAttribute('dir'),
          align: st.textAlign ? st.textAlign : null,
          alignPriority: st.getPropertyPriority ? st.getPropertyPriority('text-align') : '',
          dirStyle: st.direction ? st.direction : null,
          dirPriority: st.getPropertyPriority ? st.getPropertyPriority('direction') : '',
          ub: st.unicodeBidi ? st.unicodeBidi : null
        };
        if (registry) registry.set(el, prev); else el.__rtlfixerPrev = prev;
      }
      el.setAttribute('data-rtlfixer', marker);
      el.setAttribute('dir', decision.direction);
      if (el.style) {
        // Chat apps sometimes mark list alignment/direction as !important. Use
        // the same priority while active, then restore the exact original value
        // and priority so bullets and numbered items move with their RTL text.
        // A table gets direction only: text-align would cascade into every cell
        // and drag numeric or code columns along with the heading text.
        if (!directionOnly) {
          el.style.setProperty('text-align', decision.align, 'important');
          el.style.unicodeBidi = 'isolate';
        }
        el.style.setProperty('direction', decision.direction, 'important');
      }
    }

    function restoreTrackedNode(el) {
      if (!el || !el.getAttribute || !el.getAttribute('data-rtlfixer')) return;
      var prev = registry ? registry.get(el) : el.__rtlfixerPrev;
      if (prev) {
        if (prev.dir === null) el.removeAttribute('dir'); else el.setAttribute('dir', prev.dir);
        if (el.style) {
          if (prev.align === null) el.style.removeProperty('text-align');
          else el.style.setProperty('text-align', prev.align, prev.alignPriority || '');
          if (prev.dirStyle === null) el.style.removeProperty('direction');
          else el.style.setProperty('direction', prev.dirStyle, prev.dirPriority || '');
          el.style.unicodeBidi = prev.ub === null ? '' : prev.ub;
        }
      } else {
        el.removeAttribute('dir');
        if (el.style) { el.style.textAlign = ''; el.style.direction = ''; el.style.unicodeBidi = ''; }
      }
      el.removeAttribute('data-rtlfixer');
    }

    function isProtected(el) {
      if (!el || !el.matches) return false;
      var list = [].concat(CFG.protected || []);
      if (CFG.codeBlock) list.push(CFG.codeBlock);
      if (CFG.inlineCode) list.push(CFG.inlineCode);
      for (var i = 0; i < list.length; i++) {
        try {
          if (el.matches(list[i]) || (el.closest && el.closest(list[i]))) return true;
        } catch (e) {}
      }
      return false;
    }

    // Block-level tags. A DIV/TD that contains any of these is a layout
    // container, not a paragraph, so flipping it would rotate whole regions of
    // the app. But a DIV/TD whose children are ALL inline (span/a/strong/code/
    // bdi/img/…) is a real text block: many chat UIs render user bubbles and
    // streamed answers exactly this way. Skipping those was the main cause of
    // Persian sentences that stayed left-aligned, so we now classify them.
    var BLOCK_CHILD_TAGS = {
      DIV: 1, P: 1, UL: 1, OL: 1, LI: 1, TABLE: 1, THEAD: 1, TBODY: 1, TFOOT: 1,
      TR: 1, TD: 1, TH: 1, SECTION: 1, ARTICLE: 1, HEADER: 1, FOOTER: 1, ASIDE: 1,
      NAV: 1, MAIN: 1, FIGURE: 1, FIGCAPTION: 1, BLOCKQUOTE: 1, PRE: 1, HR: 1,
      FORM: 1, DL: 1, DT: 1, DD: 1, DETAILS: 1, SUMMARY: 1,
      H1: 1, H2: 1, H3: 1, H4: 1, H5: 1, H6: 1
    };
    function hasBlockChild(el) {
      var kids = el.children;
      if (!kids) return false;
      for (var i = 0; i < kids.length; i++) {
        if (BLOCK_CHILD_TAGS[kids[i].tagName] === 1) return true;
      }
      return false;
    }

    // A table is a container, so the block scanner skips it — but a table whose
    // content is RTL still reads wrong left-to-right, because its COLUMN order
    // is decided by the table's own direction, not by its cells. Setting
    // direction on the table alone flips the columns; each cell is still
    // classified separately for its own text.
    function processTable(el) {
      if (!el || !el.getAttribute) return;
      var text = el.textContent || '';
      if (!text.trim()) return;
      if (isProtected(el)) { restoreTrackedNode(el); return; }
      var d = Rules.classify(text);
      if (d.protected || d.direction !== 'rtl') { restoreTrackedNode(el); return; }
      // Reuses markNode so the original dir/direction are recorded and restored
      // exactly, the same as every other node the fixer touches.
      markNode(el, d, 'applied-table', true);
    }

    function processBlock(el) {
      if (!el || !el.getAttribute) return;
      // A generic DIV/TD/ARTICLE/SECTION is a text block only when it has no
      // block-level child; otherwise it is a container and must not be
      // flipped. P/LI/headings/blockquote may always hold inline children and
      // are safe to classify.
      var tag = el.tagName;
      if ((tag === 'DIV' || tag === 'TD' || tag === 'ARTICLE' || tag === 'SECTION') && hasBlockChild(el)) return;
      var text = el.textContent || '';
      if (!text.trim()) return;
      var insideProtected = isProtected(el);
      var d = Rules.classifyNode(text, insideProtected);
      if (d.protected) { restoreTrackedNode(el); return; }
      // Only touch nodes that actually need RTL. Leaving LTR nodes untouched keeps
      // our footprint minimal and avoids re-aligning centered/right UI text now
      // that the scan also covers generic leaf divs.
      if (d.direction !== 'rtl') { restoreTrackedNode(el); return; }
      markNode(el, d);
    }

    function isCandidate(el, selector) {
      if (!el || !el.matches) return false;
      try { return el.matches(selector); } catch (e) { return false; }
    }

    // Cache the chat-container lookup. querySelector runs on the target app's
    // main thread; calling it once per mutation during a streaming render burst
    // is enough to make a busy chat app feel sluggish. isConnected is O(1) and
    // lets us drop a stale node (e.g. the whole chat root was replaced) and
    // re-query only then.
    //
    // Profile selectors are best-effort guesses per app and are frequently
    // wrong (app updates, unverified profiles, apps we've never inspected). If
    // CFG.chatContainer matches nothing, falling back to document.body means
    // the scanner still runs instead of silently doing nothing forever: a
    // slightly wider scan is far better than a fixer that never touches the
    // page and lets the browser's native first-strong-character bidi algorithm
    // (which reads LTR the instant a line starts with a Latin word) decide.
    var chatRootCache = null;
    function getChatRoot() {
      if (chatRootCache && chatRootCache.isConnected) return chatRootCache;
      chatRootCache = document.querySelector(CFG.chatContainer) || document.body;
      return chatRootCache;
    }

    // Precompute the block selector once instead of rebuilding it on every scan.
    var BLOCK_SELECTOR = (function () {
      var sel = [];
      if (CFG.userMessage) sel.push(CFG.userMessage);
      if (CFG.assistantMessage) sel.push(CFG.assistantMessage);
      // Every element that normally holds a run of text directly. TH was
      // missing, so a table's HEADER cells were never classified while its body
      // cells were — a Persian table came out with its headings still
      // left-aligned. H6, CAPTION, FIGCAPTION, DT and SUMMARY were missing for
      // the same reason.
      sel.push('p','li','h1','h2','h3','h4','h5','h6','blockquote',
               'td','th','caption','dd','dt','figcaption','summary',
               'div','article','section');
      return sel.join(', ');
    })();

    function scanSubtree(root) {
      if (!root) return;
      if (root.nodeType !== 1) root = root.parentElement;
      if (!root || !root.querySelectorAll) return;
      try {
        if (isCandidate(root, BLOCK_SELECTOR)) processBlock(root);
        var nodes = root.querySelectorAll(BLOCK_SELECTOR);
        for (var i = 0; i < nodes.length; i++) processBlock(nodes[i]);
        if (isCandidate(root, 'table')) processTable(root);
        var tables = root.querySelectorAll('table');
        for (var t = 0; t < tables.length; t++) processTable(tables[t]);
      } catch (e) {}
    }

    function fixComposer(el) {
      if (!el) return;
      var text = el.value || el.textContent || '';
      var d = Rules.classify(text);
      markNode(el, d, 'applied-composer');
    }

    // --- MutationObserver (debounced) ---
    var timer = null;
    var pending = [];
    function flush() {
      timer = null;
      var batch = pending.splice(0, pending.length);
      for (var i = 0; i < batch.length; i++) scanSubtree(batch[i]);
      // Once per batch, not once per scanned node: keeps the composer binding
      // current (it can be swapped out) without repeating a querySelector for
      // every block touched during a burst.
      installComposer();
    }
    function schedule(node) {
      var target = node && node.nodeType === 1 ? node : (node && node.parentElement) || document.body;
      var chatRoot = getChatRoot();
      if (!chatRoot || !(target === chatRoot || chatRoot.contains(target) || target.contains(chatRoot))) return;
      // Collapse nested work and cap bursts from streaming/render-heavy UIs.
      for (var i = pending.length - 1; i >= 0; i--) {
        if (pending[i] === target || pending[i].contains(target)) return;
        if (target.contains(pending[i])) pending.splice(i, 1);
      }
      pending.push(target);
      if (pending.length > 50) pending = [chatRoot];
      if (timer) return;
      timer = setTimeout(flush, 80);
    }

    function startObserver() {
      var root = getChatRoot();
      if (!document.body || typeof MutationObserver === 'undefined') return;
      var obs = new MutationObserver(function (muts) {
        for (var i = 0; i < muts.length; i++) {
          var added = muts[i].addedNodes;
          for (var j = 0; j < added.length; j++) schedule(added[j]);
          if (muts[i].type === 'characterData') schedule(muts[i].target);
        }
      });
      // Observe body so replacing the entire chat root does not orphan the
      // fixer. schedule() strictly rejects changes outside the chat surface.
      obs.observe(document.body, { childList: true, characterData: true, subtree: true });
      window.__rtlfixerObserver = obs;
      if (root) scanSubtree(root);
    }

    // --- copy interceptor (scoped to CopyRoot) ---
    function onCopy(e) {
      if (CFG.copyMode === 'Original') return;
      var root = document.querySelector(CFG.copyRoot) || document.body;
      if (!root || !root.contains(e.target)) return;
      var sel = window.getSelection();
      if (!sel || sel.isCollapsed) return;
      var text = sel.toString();
      if (!text) return;
      var d = Rules.classify(text);
      var plain = Rules.buildPlainText(text, CFG.copyMode);
      var html = Rules.buildHtml(text, CFG.copyMode, d.direction);
      if (e.clipboardData && e.clipboardData.setData) {
        try {
          e.clipboardData.setData('text/plain', plain);
          e.clipboardData.setData('text/html', html);
          e.preventDefault();
        } catch (err) {}
      }
    }

    function installCopy() {
      document.addEventListener('copy', onCopy, true);
      window.__rtlfixerOnCopy = onCopy;
    }

    function installComposer() {
      var composer = document.querySelector(CFG.composer);
      if (!composer) return;
      if (window.__rtlfixerComposer === composer) return;
      if (window.__rtlfixerComposer && window.__rtlfixerOnComposerInput) {
        window.__rtlfixerComposer.removeEventListener('input', window.__rtlfixerOnComposerInput);
        window.__rtlfixerComposer.removeEventListener('keyup', window.__rtlfixerOnComposerInput);
      }
      window.__rtlfixerComposer = composer;
      function onInput() { fixComposer(composer); }
      composer.addEventListener('input', onInput);
      composer.addEventListener('keyup', onInput);
      window.__rtlfixerOnComposerInput = onInput;
      // Set initial direction.
      fixComposer(composer);
    }

    startObserver();
    installCopy();
    installComposer();

    window.__rtlfixerRestore = function () {
      try { if (window.__rtlfixerObserver) window.__rtlfixerObserver.disconnect(); } catch (e) {}
      try {
        if (window.__rtlfixerOnCopy)
          document.removeEventListener('copy', window.__rtlfixerOnCopy, true);
      } catch (e) {}
      try {
        if (window.__rtlfixerComposer && window.__rtlfixerOnComposerInput) {
          window.__rtlfixerComposer.removeEventListener('input', window.__rtlfixerOnComposerInput);
          window.__rtlfixerComposer.removeEventListener('keyup', window.__rtlfixerOnComposerInput);
        }
      } catch (e) {}
      var all = document.querySelectorAll('[data-rtlfixer]');
      for (var i = 0; i < all.length; i++) {
        restoreTrackedNode(all[i]);
      }
      window['__rtlfixerInstalled'] = false;
    };
";

    private static string JsStr(string? s) => s is null ? "null" : JsonSerializer.Serialize(s);
}
