namespace CspPaletteCompanion.Core.Settings;

/// <summary>
/// User-controlled permissions for image acquisition.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Allows read-only canvas capture through CSP's Companion Mode protocol.
    /// </summary>
    public bool AllowCompanionCanvasCapture { get; init; } = true;

    /// <summary>
    /// Allows capture modes that temporarily use the Windows clipboard.
    /// </summary>
    public bool AllowClipboardCapture { get; init; }

    /// <summary>
    /// Allows the app to execute a user-selected CSP Auto Action.
    /// Clipboard permission is still checked separately by consumers.
    /// </summary>
    public bool AllowAutoActionExecution { get; init; }

    /// <summary>
    /// Exact Companion protocol identity of the user-selected Quick Access command.
    /// Display names are stored only to make the Settings screen understandable.
    /// </summary>
    public string? SelectedAutoActionCommandType { get; init; }

    public string? SelectedAutoActionCommandName { get; init; }

    public string? SelectedAutoActionDisplayName { get; init; }

    /// <summary>
    /// Last window position. A settings file written before these members existed maps
    /// them to null, so no schema-version bump is required.
    /// </summary>
    public double? WindowLeft { get; init; }

    public double? WindowTop { get; init; }

    /// <summary>
    /// True: the window lives in the notification area. The close button hides it, the
    /// app keeps running, and Exit lives in the tray menu. False: the window owns a
    /// taskbar button for its whole life and the close button exits.
    /// A settings.json written before this member existed has no such property, so
    /// System.Text.Json leaves this initialiser in place and the app starts in tray
    /// mode. That is the intended default; the one-time hint covers the change.
    /// </summary>
    public bool RunInTray { get; init; } = true;

    /// <summary>
    /// Set the first time the window is hidden to the tray, so the one-time
    /// "still running" balloon is shown once per user and never again.
    /// </summary>
    public bool TrayHintShown { get; init; }

    /// <summary>
    /// Reads the CSP Mux session handoff file and connects through the proxy
    /// instead of scanning CSP's QR code.
    /// </summary>
    public bool UseMuxWhenAvailable { get; init; }

    public static AppSettings Defaults => new();

    public bool IsAllowed(CapturePermission permission) =>
        permission switch
        {
            CapturePermission.CompanionCanvasCapture => AllowCompanionCanvasCapture,
            CapturePermission.ClipboardCapture => AllowClipboardCapture,
            CapturePermission.AutoActionExecution => AllowAutoActionExecution,
            _ => false,
        };
}
