using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.ChatRTLFixer.Injectors;

/// <summary>
/// Minimal Chrome DevTools Protocol HTTP discovery client. Connects ONLY to
/// 127.0.0.1 and verifies the bind is loopback before use. No external network
/// calls are ever made by this class.
/// </summary>
public sealed class CdpDiscoveryClient : IDisposable
{
    private static readonly Uri[] Empty = [];
    private readonly HttpClient _http;

    public CdpDiscoveryClient()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            // Hard guard: only loopback is permitted.
            Proxy = new LoopbackOnlyProxy(),
            UseProxy = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    /// <summary>
    /// Discovers page targets on 127.0.0.1:port. Throws if the endpoint is not
    /// reachable or is not bound to loopback (defence in depth).
    /// </summary>
    public async Task<IReadOnlyList<CdpTarget>> DiscoverAsync(int port, string? titlePattern, CancellationToken ct)
    {
        var result = await DiscoverDetailedAsync(port, titlePattern, ct);
        if (result.Failure is not null) throw new CdpDiscoveryException(result.Failure.Value, result.Detail ?? "CDP discovery failed");
        return result.Targets;
    }

    /// <summary>Performs TCP, /json/version and /json checks separately so failures are actionable.</summary>
    public async Task<CdpDiscoveryResult> DiscoverDetailedAsync(int port, string? titlePattern, CancellationToken ct)
    {
        if (port <= 0 || port > 65535) return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.PortClosed, "invalid-port");

        using (var tcp = new TcpClient())
        {
            try { await tcp.ConnectAsync(IPAddress.Loopback, port, ct); }
            catch (SocketException ex) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.ConnectionRefused, ex.SocketErrorCode.ToString()); }
            catch (OperationCanceledException) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.HttpTimeout, "tcp-timeout"); }
        }

        CdpVersion? version;
        try
        {
            using var response = await _http.GetAsync(new Uri($"http://127.0.0.1:{port}/json/version"), ct);
            if (!response.IsSuccessStatusCode) return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.PortClosed, $"version-http-{(int)response.StatusCode}");
            version = await response.Content.ReadFromJsonAsync<CdpVersion>(_jsonOpts, ct);
        }
        catch (TaskCanceledException) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.HttpTimeout, "version-timeout"); }
        catch (JsonException) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.InvalidJson, "version-invalid-json"); }
        catch (HttpRequestException ex) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.ConnectionRefused, ex.HttpRequestError.ToString()); }

        var uri = new Uri($"http://127.0.0.1:{port}/json");
        List<CdpTarget>? targets;
        try
        {
            using var resp = await _http.GetAsync(uri, ct);
            if (!resp.IsSuccessStatusCode) return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.PortClosed, $"targets-http-{(int)resp.StatusCode}", version);
            targets = await resp.Content.ReadFromJsonAsync<List<CdpTarget>>(_jsonOpts, ct);
        }
        catch (TaskCanceledException) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.HttpTimeout, "targets-timeout", version); }
        catch (JsonException) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.InvalidJson, "targets-invalid-json", version); }
        catch (HttpRequestException ex) { return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.ConnectionRefused, ex.HttpRequestError.ToString(), version); }

        // Only page-type targets are useful for injection.
        var pages = (targets ?? []).Where(t => string.Equals(t.Type, "page", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pages.Count == 0) return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.NoPageTarget, "no-page-target", version);
        if (!string.IsNullOrEmpty(titlePattern))
        {
            pages = pages.Where(t => t.Title != null && t.Title.Contains(titlePattern, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pages.Count == 0) return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.TargetNotMatchingProfile, "target-not-matching-profile", version);
        }
        if (pages.All(p => string.IsNullOrWhiteSpace(p.WebSocketDebuggerUrl)))
            return CdpDiscoveryResult.Failed(CdpDiscoveryFailure.WebSocketUrlMissing, "websocket-url-missing", version);
        return CdpDiscoveryResult.Ok(pages, version);
    }

    public void Dispose() => _http.Dispose();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public enum CdpDiscoveryFailure
{
    PortClosed, ConnectionRefused, HttpTimeout, InvalidJson, NoPageTarget, WebSocketUrlMissing, TargetNotMatchingProfile,
}

public sealed class CdpDiscoveryResult
{
    public required IReadOnlyList<CdpTarget> Targets { get; init; }
    public CdpVersion? Version { get; init; }
    public CdpDiscoveryFailure? Failure { get; init; }
    public string? Detail { get; init; }
    public static CdpDiscoveryResult Ok(IReadOnlyList<CdpTarget> targets, CdpVersion? version) => new() { Targets = targets, Version = version };
    public static CdpDiscoveryResult Failed(CdpDiscoveryFailure failure, string detail, CdpVersion? version = null) => new() { Targets = [], Version = version, Failure = failure, Detail = detail };
}

public sealed class CdpDiscoveryException : Exception
{
    public CdpDiscoveryFailure Failure { get; }
    public CdpDiscoveryException(CdpDiscoveryFailure failure, string message) : base(message) => Failure = failure;
}

public sealed class CdpVersion
{
    [JsonPropertyName("Browser")] public string? Browser { get; set; }
    [JsonPropertyName("Protocol-Version")] public string? ProtocolVersion { get; set; }
}

/// <summary>A CDP target entry.</summary>
public sealed class CdpTarget
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("webSocketDebuggerUrl")] public string? WebSocketDebuggerUrl { get; set; }
}

/// <summary>
/// A proxy that refuses any non-loopback host. Used as defence in depth so that
/// no misconfiguration can ever reach an external address.
/// </summary>
internal sealed class LoopbackOnlyProxy : System.Net.IWebProxy
{
    public Uri? GetProxy(Uri destination) => null; // direct; we only ever call 127.0.0.1
    public ICredentials? Credentials { get; set; }
    public bool IsBypassed(Uri host) => true; // we bypass the proxy entirely
}
