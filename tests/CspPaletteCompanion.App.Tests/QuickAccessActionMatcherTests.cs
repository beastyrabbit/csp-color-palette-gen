using CspPaletteCompanion.App;
using CspPaletteCompanion.Companion;

namespace CspPaletteCompanion.App.Tests;

public sealed class QuickAccessActionMatcherTests
{
    [Theory]
    [InlineData("Sichtbare Ebenen kopieren", "PaletteCommand", "My actions")]
    [InlineData("Copy merged selection", "PaletteCommand", "My actions")]
    [InlineData("Renamed action", "PaletteCommand", "CSP Palette Companion")]
    public void RecognizesSetupActionNames(
        string displayName,
        string commandName,
        string setName)
    {
        Assert.True(QuickAccessActionMatcher.IsRecommended(
            Choice(displayName, commandName, setName)));
    }

    [Fact]
    public void HidesUnrelatedCommandsWhenARecommendedActionExists()
    {
        var recommended = Choice(
            "Sichtbare Ebenen kopieren",
            "recommended",
            "My actions");
        var unrelated = Choice("Undo", "undo", "Quick Access");

        var visible = QuickAccessActionMatcher.VisibleChoices(
            [unrelated, recommended],
            selected: null,
            showAll: false);

        Assert.Equal([recommended], visible);
    }

    [Fact]
    public void KeepsAnExistingCustomSelectionVisible()
    {
        var recommended = Choice(
            "Sichtbare Ebenen kopieren",
            "recommended",
            "My actions");
        var selected = Choice("My renamed action", "custom", "My actions");

        var visible = QuickAccessActionMatcher.VisibleChoices(
            [recommended, selected],
            selected.Identity,
            showAll: false);

        Assert.Equal([recommended, selected], visible);
    }

    [Fact]
    public void FallsBackToAllCommandsWhenNothingCanBeRecognized()
    {
        CompanionQuickAccessCommandChoice[] choices =
        [
            Choice("My renamed action", "custom", "My actions"),
            Choice("Undo", "undo", "Quick Access"),
        ];

        var visible = QuickAccessActionMatcher.VisibleChoices(
            choices,
            selected: null,
            showAll: false);

        Assert.Same(choices, visible);
    }

    [Fact]
    public void ShowAllReturnsEveryCommand()
    {
        CompanionQuickAccessCommandChoice[] choices =
        [
            Choice("Sichtbare Ebenen kopieren", "recommended", "My actions"),
            Choice("Undo", "undo", "Quick Access"),
        ];

        var visible = QuickAccessActionMatcher.VisibleChoices(
            choices,
            selected: null,
            showAll: true);

        Assert.Same(choices, visible);
    }

    private static CompanionQuickAccessCommandChoice Choice(
        string displayName,
        string commandName,
        string setName) =>
        new(
            new CompanionQuickAccessCommandIdentity("PaletteCommand", commandName),
            displayName,
            setName,
            "set-uuid",
            0,
            0,
            0,
            0);
}
