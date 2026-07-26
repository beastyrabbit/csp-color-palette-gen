using CspPaletteCompanion.Core.Settings;

namespace CspPaletteCompanion.Core.Tests;

public sealed class AppSettingsServiceTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CspPaletteCompanion.Settings.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Defaults_AllowOnlyCompanionCanvasCapture()
    {
        AppSettings settings = AppSettings.Defaults;

        Assert.True(settings.IsAllowed(CapturePermission.CompanionCanvasCapture));
        Assert.False(settings.IsAllowed(CapturePermission.ClipboardCapture));
        Assert.False(settings.IsAllowed(CapturePermission.AutoActionExecution));
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefaults()
    {
        AppSettingsService service = CreateService();

        AppSettings settings = await service.LoadAsync();

        Assert.Equal(AppSettings.Defaults, settings);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsPermissions()
    {
        AppSettingsService service = CreateService();
        AppSettings expected = new()
        {
            AllowClipboardCapture = true,
            AllowAutoActionExecution = true,
        };

        await service.SaveAsync(expected);
        AppSettings actual = await service.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsCorrupt_ReturnsDefaults()
    {
        AppSettingsService service = CreateService();
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(service.SettingsFilePath, "{ not JSON");

        AppSettings settings = await service.LoadAsync();

        Assert.Equal(AppSettings.Defaults, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenPropertiesAreMissing_UsesSafePropertyDefaults()
    {
        AppSettingsService service = CreateService();
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(service.SettingsFilePath, "{}");

        AppSettings settings = await service.LoadAsync();

        Assert.Equal(AppSettings.Defaults, settings);
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingFileWithoutLeavingTemporaryFiles()
    {
        AppSettingsService service = CreateService();
        await service.SaveAsync(new() { AllowClipboardCapture = true });

        AppSettings expected = new() { AllowAutoActionExecution = true };
        await service.SaveAsync(expected);

        Assert.Equal(expected, await service.LoadAsync());
        Assert.Empty(Directory.EnumerateFiles(testDirectory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private AppSettingsService CreateService() =>
        new(Path.Combine(testDirectory, AppSettingsService.SettingsFileName));
}
