using System.Text.Json;

namespace CspPaletteCompanion.Core.Settings;

/// <summary>
/// Loads and atomically persists application settings in the user's local profile.
/// </summary>
public sealed class AppSettingsService
{
    public const string ApplicationFolderName = "CSP Palette Companion";
    public const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim saveLock = new(1, 1);

    public AppSettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? GetDefaultSettingsFilePath();
    }

    public string SettingsFilePath { get; }

    public static string GetDefaultSettingsFilePath()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(localAppData, ApplicationFolderName, SettingsFileName);
    }

    /// <summary>
    /// Returns safe defaults when the settings file is absent, unreadable, or corrupt.
    /// </summary>
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using FileStream stream = new(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            AppSettings? settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            return settings is null ? AppSettings.Defaults : Normalize(settings);
        }
        catch (Exception exception) when (IsRecoverableLoadFailure(exception))
        {
            return AppSettings.Defaults;
        }
    }

    /// <summary>
    /// Writes to a sibling temporary file before replacing the live settings file.
    /// </summary>
    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        byte[] contents = JsonSerializer.SerializeToUtf8Bytes(
            Normalize(settings),
            SerializerOptions);

        await saveLock.WaitAsync(cancellationToken);
        string? temporaryPath = null;

        try
        {
            string directory = Path.GetDirectoryName(SettingsFilePath)
                ?? throw new InvalidOperationException(
                    "The settings file path must include a directory.");

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(SettingsFilePath)}.{Guid.NewGuid():N}.tmp");

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }

            saveLock.Release();
        }
    }

    private static AppSettings Normalize(AppSettings settings) =>
        settings with { SchemaVersion = AppSettings.CurrentSchemaVersion };

    private static bool IsRecoverableLoadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException;

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A failed save must not be obscured by best-effort temp-file cleanup.
        }
    }
}
