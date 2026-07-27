using CspPaletteCompanion.App;

namespace CspPaletteCompanion.App.Tests;

public sealed class ClipboardRestorationTests
{
    [Fact]
    public async Task GuaranteedRestore_ReturnsTheOperationResultAndRestoreStatus()
    {
        var restoreCalls = 0;

        var (result, restored) =
            await CspAcquisitionService.RunWithGuaranteedRestoreAsync(
                () => Task.FromResult(42),
                () =>
                {
                    restoreCalls++;
                    return true;
                });

        Assert.Equal(42, result);
        Assert.True(restored);
        Assert.Equal(1, restoreCalls);
    }

    [Fact]
    public async Task GuaranteedRestore_RunsWhenTheOperationFails()
    {
        var restored = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CspAcquisitionService.RunWithGuaranteedRestoreAsync<int>(
                () => throw new InvalidOperationException("failed after copying"),
                () => restored = true));

        Assert.True(restored);
    }

    [Fact]
    public async Task GuaranteedRestore_RunsWhenTheOperationIsCancelled()
    {
        var restored = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CspAcquisitionService.RunWithGuaranteedRestoreAsync<int>(
                () => Task.FromCanceled<int>(new CancellationToken(canceled: true)),
                () => restored = true));

        Assert.True(restored);
    }
}
