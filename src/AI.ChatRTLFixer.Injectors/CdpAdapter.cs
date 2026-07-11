using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingCommands = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
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

        var discovery = await _discovery.DiscoverDetailedAsync(port, cdp.TargetTitlePattern, ct);
        if (discovery.Failure is not null)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Cdp, "tcp-check-failed", ("port", port), ("reason", ToLogReason(discovery.Failure.Value)));
            return AttachResult.Failed(ToAttachFailure(discovery.Failure.Value), discovery.Detail ?? "discovery-failed");
        }
        var targets = discovery.Targets;
        if (discovery.Version is { } version)
            _logger.Log(LogLevel.Information, LogCategories.Cdp, "version-ok", ("port", port), ("browser", version.Browser ?? "unknown"), ("protocol", version.ProtocolVersion ?? "unknown"));

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
            _listenCts?.Cancel();
            await DisposeSocketAsync();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), ct);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogCategories.Cdp, "ws-connect-failed");
            return AttachResult.Failed(AttachFailure.WebSocketFailed, SafeLogger.Redact(ex.Message));
        }

        _logger.Log(LogLevel.Information, LogCategories.Cdp, "attached", ("bind", bind), ("port", port));
        _logger.Log(LogLevel.Information, LogCategories.Cdp, "target-selected", ("targetType", target.Type ?? "unknown"));
        StartListening();
        return AttachResult.Ok(bind);
    }

    public async Task InjectAsync(InjectionPayload payload, CancellationToken ct)
    {
        if (!IsAttached) throw new InvalidOperationException("not attached");

        // Font style (complete @font-face + scoped font-family) via one style element.
        if (!string.IsNullOrEmpty(payload.FontCss))
        {
            await InjectStyleAsync(Constants.FontStyleId, payload.FontCss, ct);
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

    private void StartListening()
    {
        var socket = _ws ?? throw new InvalidOperationException("CDP websocket is not connected.");
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        var listenCts = new CancellationTokenSource();
        _listenCts = listenCts;
        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[8192];
                while (socket.State == WebSocketState.Open && !listenCts.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await socket.ReceiveAsync(buffer, listenCts.Token);
                        if (res.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                    } while (!res.EndOfMessage);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    DispatchResponse(sb.ToString());
                }
            }
            catch
            {
                // Detached.
            }
            finally
            {
                if (ReferenceEquals(socket, _ws))
                    FailPendingCommands(new InvalidOperationException("CDP connection closed before a command completed."));
            }
            if (ReferenceEquals(socket, _ws)) Detached?.Invoke(this, EventArgs.Empty);
        }, listenCts.Token);
    }

    private async Task InjectStyleAsync(string id, string css, CancellationToken ct)
    {
        var escaped = JsonEscape(css);
        var js = $"(function(){{var e=document.getElementById('{id}');if(!e){{e=document.createElement('style');e.id='{id}';document.head.appendChild(e);}}e.textContent={EscapedText(escaped)};}})();";
        await EvaluateAsync(js, ct);
    }

    private async Task RemoveStyleAsync(string id, CancellationToken ct)
    {
        var js = $"(function(){{var e=document.getElementById('{id}');if(e)e.parentNode.removeChild(e);}})();";
        await EvaluateAsync(js, ct);
    }

    private async Task EvaluateAsync(string expression, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("CDP websocket is not connected.");

        var id = Interlocked.Increment(ref _msgId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingCommands.TryAdd(id, completion))
            throw new InvalidOperationException("Unable to register CDP command.");

        var msg = JsonSerializer.Serialize(new
        {
            id = id,
            method = "Runtime.evaluate",
            @params = new { expression = expression, returnByValue = false },
        });
        var bytes = Encoding.UTF8.GetBytes(msg);
        try
        {
            var enteredSendGate = false;
            try
            {
                await _sendGate.WaitAsync(ct);
                enteredSendGate = true;
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            finally
            {
                if (enteredSendGate) _sendGate.Release();
            }

            using var registration = ct.Register(() => completion.TrySetCanceled(ct));
            var response = await completion.Task;
            ThrowIfCommandFailed(response);
        }
        finally
        {
            _pendingCommands.TryRemove(id, out _);
        }
    }

    private void DispatchResponse(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idProperty) || !idProperty.TryGetInt32(out var id)) return;
            if (_pendingCommands.TryRemove(id, out var completion))
                completion.TrySetResult(root.Clone());
        }
        catch (JsonException)
        {
            // CDP events that are not JSON commands cannot make injection appear successful.
        }
    }

    private static void ThrowIfCommandFailed(JsonElement response)
    {
        if (response.TryGetProperty("error", out var error))
            throw new InvalidOperationException("CDP command failed: " + error.GetRawText());

        if (response.TryGetProperty("result", out var result) &&
            result.TryGetProperty("exceptionDetails", out var exception))
            throw new InvalidOperationException("Injected JavaScript failed: " + exception.GetRawText());
    }

    private void FailPendingCommands(Exception error)
    {
        foreach (var pair in _pendingCommands)
        {
            if (_pendingCommands.TryRemove(pair.Key, out var completion))
                completion.TrySetException(error);
        }
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

    private static string ToLogReason(CdpDiscoveryFailure failure) => failure switch
    {
        CdpDiscoveryFailure.PortClosed => "port-closed",
        CdpDiscoveryFailure.ConnectionRefused => "connection-refused",
        CdpDiscoveryFailure.HttpTimeout => "http-timeout",
        CdpDiscoveryFailure.InvalidJson => "invalid-json",
        CdpDiscoveryFailure.NoPageTarget => "no-page-target",
        CdpDiscoveryFailure.WebSocketUrlMissing => "websocket-url-missing",
        CdpDiscoveryFailure.TargetNotMatchingProfile => "target-not-matching-profile",
        _ => "unknown",
    };

    private static AttachFailure ToAttachFailure(CdpDiscoveryFailure failure) => failure switch
    {
        CdpDiscoveryFailure.PortClosed => AttachFailure.PortClosed,
        CdpDiscoveryFailure.ConnectionRefused => AttachFailure.ConnectionRefused,
        CdpDiscoveryFailure.HttpTimeout => AttachFailure.Timeout,
        CdpDiscoveryFailure.InvalidJson => AttachFailure.InvalidJson,
        CdpDiscoveryFailure.NoPageTarget => AttachFailure.NoPageTarget,
        CdpDiscoveryFailure.WebSocketUrlMissing => AttachFailure.WebSocketUrlMissing,
        CdpDiscoveryFailure.TargetNotMatchingProfile => AttachFailure.TargetNotMatchingProfile,
        _ => AttachFailure.DiscoveryFailed,
    };

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _listenCts?.Cancel();
        FailPendingCommands(new ObjectDisposedException(nameof(CdpAdapter)));
        await DisposeSocketAsync();
        _discovery.Dispose();
        _listenCts?.Dispose();
        _sendGate.Dispose();
    }

    private async Task DisposeSocketAsync()
    {
        if (_ws is null) return;
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "exit", CancellationToken.None);
        }
        catch { }
        _ws.Dispose();
        _ws = null;
    }
}
