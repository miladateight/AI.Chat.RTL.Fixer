using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Injectors;

/// <summary>
/// Attaches to an Electron app via Chrome DevTools Protocol on 127.0.0.1, and
/// injects font/CSS/script. Verifies the debug endpoint is bound to loopback.
/// Never makes external network calls.
/// </summary>
public sealed class CdpAdapter : ITargetAdapter
{
    private readonly SafeLogger _logger;
    private readonly CdpDiscoveryClient _discovery;
    private ClientWebSocket? _ws;
    private int _msgId;
    private CancellationTokenSource? _listenCts;
    private bool _disposed;

    public bool IsAttached => _ws is { State: WebSocketState.Open };

    public event EventHandler? Detached;

    public CdpAdapter(SafeLogger logger)
    {
        _logger = logger;
        _discovery = new CdpDiscoveryClient();
    }

    public Task<AttachResult> AttachAsync(AppProfile profile, CancellationToken ct)
    {
        if (profile.Cdp is null)
            return Task.FromResult(AttachResult.Failed(AttachFailure.Unknown, "profile has no CDP strategy"));

        // The orchestrator resolves the port (already-open or relaunched) and
        // calls AttachToPortAsync. This generic AttachAsync signals that a port
        // is required.
        return Task.FromResult(AttachResult.Failed(AttachFailure.NoDebugPort, "AttachAsync requires the orchestrator to supply the port"));
    }

    /// <summary>
    /// Attach to a specific already-open debug port on 127.0.0.1. Verifies the
    /// endpoint is loopback and a matching page target exists.
    /// </summary>
    public async Task<AttachResult> AttachToPortAsync(AppProfile profile, int port, CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CdpAdapter));
        var cdp = profile.Cdp!;
        var bind = Constants.LoopbackAddress;

        IReadOnlyList<CdpTarget> targets;
        try
        {
            targets = await _discovery.DiscoverAsync(port, cdp.TargetTitlePattern, ct);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogCategories.Cdp, "discovery-failed", ("port", port));
            return AttachResult.Failed(AttachFailure.DiscoveryFailed, SafeLogger.Redact(ex.Message));
        }

        if (targets.Count == 0)
            return AttachResult.Failed(AttachFailure.NoMatchingTarget, "no matching page target", false);

        var target = targets[0];
        if (string.IsNullOrEmpty(target.WebSocketDebuggerUrl))
            return AttachResult.Failed(AttachFailure.WebSocketFailed, "no webSocketDebuggerUrl");

        // Defence in depth: refuse any ws url that is not 127.0.0.1 / localhost.
        if (!IsLoopbackWsUrl(target.WebSocketDebuggerUrl))
        {
            _logger.Log(LogLevel.Error, LogCategories.Security, "ws-not-loopback", ("host", HostOf(target.WebSocketDebuggerUrl)));
            return AttachResult.Failed(AttachFailure.BindNotLoopback, "debug endpoint is not bound to 127.0.0.1");
        }

        try
        {
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), ct);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogCategories.Cdp, "ws-connect-failed");
            return AttachResult.Failed(AttachFailure.WebSocketFailed, SafeLogger.Redact(ex.Message));
        }

        _logger.Log(LogLevel.Information, LogCategories.Cdp, "attached", ("bind", bind), ("port", port));
        StartListening();
        return AttachResult.Ok(bind);
    }

    public async Task InjectAsync(InjectionPayload payload, CancellationToken ct)
    {
        if (!IsAttached) throw new InvalidOperationException("not attached");

        // Font + CSS via a single style element each.
        if (!string.IsNullOrEmpty(payload.FontBase64))
        {
            var fontCss = BuildFontStyleFromPayload(payload);
            await InjectStyleAsync(Constants.FontStyleId, fontCss, ct);
        }
        await InjectStyleAsync(Constants.CssStyleId, payload.Css, ct);

        // Runtime script via Runtime.evaluate.
        await EvaluateAsync(payload.Script, ct);
        _logger.Log(LogLevel.Information, LogCategories.Injector, "injected");
    }

    public async Task RestoreAsync(CancellationToken ct)
    {
        if (!IsAttached) return;
        try
        {
            await EvaluateAsync("window.__rtlfixerRestore && window.__rtlfixerRestore();", ct);
            await RemoveStyleAsync(Constants.CssStyleId, ct);
            await RemoveStyleAsync(Constants.FontStyleId, ct);
            _logger.Log(LogLevel.Information, LogCategories.Restore, "restored");
        }
        catch (Exception)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Restore, "restore-partial");
        }
    }

    private static string BuildFontStyleFromPayload(InjectionPayload p)
    {
        // The font CSS is built by the host (FontPack) for non-base64 cases; here
        // we just inline the provided base64 as an @font-face.
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(p.FontBase64))
        {
            sb.Append("@font-face { font-family: \"Vazirmatn\"; font-style: normal; font-weight: 100 900; font-display: swap; ");
            sb.Append("src: url(data:font/ttf;base64,").Append(p.FontBase64).Append(") format('truetype'); }\n");
        }
        sb.Append(p.FontFamilyCss);
        return sb.ToString();
    }

    private void StartListening()
    {
        _listenCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[8192];
                while (_ws is { State: WebSocketState.Open } && !_listenCts.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await _ws.ReceiveAsync(buffer, _listenCts.Token);
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                    } while (!res.EndOfMessage);
                    // We do not need to parse responses for injection; discard.
                }
            }
            catch
            {
                // Detached.
            }
            Detached?.Invoke(this, EventArgs.Empty);
        }, _listenCts.Token);
    }

    private async Task InjectStyleAsync(string id, string css, CancellationToken ct)
    {
        var escaped = JsonEscape(css);
        var js = $"(function(){{var e=document.getElementById('{id}');if(e)return;e=document.createElement('style');e.id='{id}';e.textContent={EscapedText(escaped)};document.head.appendChild(e);}})();";
        await EvaluateAsync(js, ct);
    }

    private async Task RemoveStyleAsync(string id, CancellationToken ct)
    {
        var js = $"(function(){{var e=document.getElementById('{id}');if(e)e.parentNode.removeChild(e);}})();";
        await EvaluateAsync(js, ct);
    }

    private async Task EvaluateAsync(string expression, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open) return;
        var id = Interlocked.Increment(ref _msgId);
        var msg = JsonSerializer.Serialize(new
        {
            id = id,
            method = "Runtime.evaluate",
            @params = new { expression = expression, returnByValue = false },
        });
        var bytes = Encoding.UTF8.GetBytes(msg);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static string EscapedText(string escaped) => "\"" + escaped + "\"";

    private static string JsonEscape(string s) => JsonSerializer.Serialize(s).Trim('"');

    private static bool IsLoopbackWsUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.IsLoopback || string.Equals(u.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; } catch { return "?"; }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _listenCts?.Cancel();
        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "exit", CancellationToken.None);
            }
            catch { }
            _ws.Dispose();
        }
        _discovery.Dispose();
        _listenCts?.Dispose();
    }
}