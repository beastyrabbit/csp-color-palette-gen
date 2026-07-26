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
