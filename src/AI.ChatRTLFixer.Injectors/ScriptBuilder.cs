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
    function markNode(el, decision) {
      if (!el || !el.setAttribute) return;
      if (el.getAttribute('data-rtlfixer') === 'applied') return;
      var prev = { dir: el.getAttribute('dir'), align: el.style && el.style.textAlign ? el.style.textAlign : null };
      if (registry) registry.set(el, prev); else el.__rtlfixerPrev = prev;
      el.setAttribute('data-rtlfixer', 'applied');
      el.setAttribute('dir', decision.direction);
      if (el.style) el.style.textAlign = decision.align;
    }

    function isProtected(el) {
      if (!el || !el.matches) return false;
      var list = [].concat(CFG.protected || []);
      if (CFG.codeBlock) list.push(CFG.codeBlock);
      if (CFG.inlineCode) list.push(CFG.inlineCode);
      for (var i = 0; i < list.length; i++) {
        try { if (el.matches(list[i])) return true; } catch (e) {}
      }
      return false;
    }

    function processBlock(el) {
      if (!el || !el.getAttribute) return;
      if (el.getAttribute('data-rtlfixer') === 'applied') return;
      var text = el.textContent || '';
      if (!text.trim()) return;
      var insideProtected = isProtected(el);
      var d = Rules.classifyNode(text, insideProtected);
      if (d.protected) return;
      markNode(el, d);
    }

    function scanSubtree(root) {
      if (!root || !root.querySelectorAll) return;
      var sel = [];
      if (CFG.userMessage) sel.push(CFG.userMessage);
      if (CFG.assistantMessage) sel.push(CFG.assistantMessage);
      sel.push('p','li','h1','h2','h3','h4','blockquote');
      try {
        var nodes = root.querySelectorAll(sel.join(', '));
        for (var i = 0; i < nodes.length; i++) processBlock(nodes[i]);
      } catch (e) {}
      var composer = document.querySelector(CFG.composer);
      if (composer) fixComposer(composer);
    }

    function fixComposer(el) {
      if (!el) return;
      var text = el.value || el.textContent || '';
      var d = Rules.classify(text);
      if (d.protected) return;
      el.setAttribute('data-rtlfixer', 'applied-composer');
      el.setAttribute('dir', d.direction);
      if (el.style) el.style.textAlign = d.align;
    }

    // --- MutationObserver (debounced) ---
    var timer = null;
    var pending = [];
    function flush() {
      timer = null;
      var batch = pending.splice(0, pending.length);
      for (var i = 0; i < batch.length; i++) scanSubtree(batch[i]);
    }
    function schedule(node) {
      pending.push(node);
      if (timer) return;
      timer = setTimeout(flush, 80);
    }

    function startObserver() {
      var root = document.querySelector(CFG.chatContainer) || document.body;
      if (!root || typeof MutationObserver === 'undefined') return;
      var obs = new MutationObserver(function (muts) {
        for (var i = 0; i < muts.length; i++) {
          var added = muts[i].addedNodes;
          for (var j = 0; j < added.length; j++) schedule(added[j]);
        }
      });
      obs.observe(root, { childList: true, subtree: true });
      window.__rtlfixerObserver = obs;
      scanSubtree(root);
    }

    // --- copy interceptor (scoped to CopyRoot) ---
    function onCopy(e) {
      if (CFG.copyMode === 'Original') return;
      var root = document.querySelector(CFG.copyRoot);
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
      var root = document.querySelector(CFG.copyRoot);
      if (root) root.addEventListener('copy', onCopy, true);
      window.__rtlfixerOnCopy = onCopy;
      window.__rtlfixerCopyRoot = root;
    }

    startObserver();
    installCopy();

    window.__rtlfixerRestore = function () {
      try { if (window.__rtlfixerObserver) window.__rtlfixerObserver.disconnect(); } catch (e) {}
      try {
        if (window.__rtlfixerCopyRoot && window.__rtlfixerOnCopy)
          window.__rtlfixerCopyRoot.removeEventListener('copy', window.__rtlfixerOnCopy, true);
      } catch (e) {}
      var all = document.querySelectorAll('[data-rtlfixer]');
      for (var i = 0; i < all.length; i++) {
        var el = all[i];
        var prev = registry ? registry.get(el) : el.__rtlfixerPrev;
        if (prev) {
          if (prev.dir === null) el.removeAttribute('dir'); else if (prev.dir) el.setAttribute('dir', prev.dir);
          if (el.style && prev.align === null) el.style.textAlign = ''; else if (el.style && prev.align) el.style.textAlign = prev.align;
        } else {
          el.removeAttribute('dir');
          if (el.style) el.style.textAlign = '';
        }
        el.removeAttribute('data-rtlfixer');
      }
      window['__rtlfixerInstalled'] = false;
    };
";

    private static string JsStr(string? s) => s is null ? "null" : JsonSerializer.Serialize(s);
}