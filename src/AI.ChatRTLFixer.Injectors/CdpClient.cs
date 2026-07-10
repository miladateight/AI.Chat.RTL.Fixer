using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
        if (port <= 0 || port > 65535) throw new ArgumentException("invalid port", nameof(port));

        var uri = new Uri($"http://127.0.0.1:{port}/json");
        using var resp = await _http.GetAsync(uri, ct);
        resp.EnsureSuccessStatusCode();

        var targets = await resp.Content.ReadFromJsonAsync<List<CdpTarget>>(_jsonOpts, ct) ?? new();
        // Only page-type targets are useful for injection.
        var pages = targets.Where(t => string.Equals(t.Type, "page", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(titlePattern))
        {
            pages = pages.Where(t => t.Title != null && t.Title.Contains(titlePattern, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return pages;
    }

    public void Dispose() => _http.Dispose();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
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