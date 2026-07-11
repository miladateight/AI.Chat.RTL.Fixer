using AI.ChatRTLFixer.Core.Profiles;

namespace AI.ChatRTLFixer.Core.Abstractions;

/// <summary>
/// Contract for a target-app adapter. v0.1 ships <c>CdpAdapter</c> for Electron.
/// WebView2/Tauri/Qt/WPF adapters are Planned and not implemented until tested
/// against a real app.
/// </summary>
public interface ITargetAdapter : IAsyncDisposable
{
    /// <summary>True while attached to a live target page.</summary>
    bool IsAttached { get; }

    /// <summary>Raised when the target detaches (process exit, page close, websocket drop).</summary>
    event EventHandler? Detached;

    /// <summary>Attach to the running target described by the profile.</summary>
    Task<AttachResult> AttachAsync(AppProfile profile, CancellationToken ct);

    /// <summary>Inject font, CSS and runtime script into the attached page.</summary>
    Task InjectAsync(InjectionPayload payload, CancellationToken ct);

    /// <summary>Remove every runtime modification made by this adapter.</summary>
    Task RestoreAsync(CancellationToken ct);
}

/// <summary>Outcome of an attach attempt.</summary>
public sealed class AttachResult
{
    public required bool Success { get; init; }

    /// <summary>Why attach failed, in machine-readable form. Null on success.</summary>
    public AttachFailure? Failure { get; init; }

    /// <summary>Human-readable detail for the tray/log (no chat text).</summary>
    public string? Detail { get; init; }


    /// <summary>Verified bind address of the debug endpoint. Always 127.0.0.1 on success.</summary>
    public string? VerifiedBindAddress { get; init; }

    public static AttachResult Ok(string verifiedBindAddress) => new()
    {
        Success = true,
        VerifiedBindAddress = verifiedBindAddress,
    };

    public static AttachResult Failed(AttachFailure failure, string detail) => new()
    {
        Success = false,
        Failure = failure,
        Detail = detail,
    };
}

public enum AttachFailure
{
    ProcessNotFound,
    NoDebugPort,
    PortClosed,
    ConnectionRefused,
    InvalidJson,
    NoPageTarget,
    WebSocketUrlMissing,
    TargetNotMatchingProfile,
    DiscoveryFailed,
    NoMatchingTarget,
    WebSocketFailed,
    BindNotLoopback,
    Timeout,
    Unknown,
}

/// <summary>Everything injected into a page in one pass.</summary>
public sealed class InjectionPayload
{
    /// <summary>
    /// Complete font style block (@font-face + scoped font-family), or null if
    /// no font should be injected. Built by <see cref="FontPack.BuildFontStyle"/>.
    /// </summary>
    public string? FontCss { get; init; }

    /// <summary>Built CSS for direction/alignment/protection, scoped to the profile selectors.</summary>
    public required string Css { get; init; }

    /// <summary>The canonical runtime script (rtlfixer.rules.js) plus the observer/clipboard bootstrap.</summary>
    public required string Script { get; init; }

    /// <summary>Active copy mode.</summary>
    public required CopyMode CopyMode { get; init; }
}
