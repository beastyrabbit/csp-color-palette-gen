using CspPaletteCompanion.Companion;

namespace CspPaletteCompanion.App;

internal static class QuickAccessActionMatcher
{
    internal static bool IsRecommended(CompanionQuickAccessCommand command) =>
        IsRecommended(command.DisplayName, command.CommandName, command.SetName);

    internal static bool IsRecommended(CompanionQuickAccessCommandChoice choice) =>
        IsRecommended(
            choice.DisplayName,
            choice.Identity.CommandName,
            choice.SetName);

    internal static IReadOnlyList<CompanionQuickAccessCommandChoice> VisibleChoices(
        IReadOnlyList<CompanionQuickAccessCommandChoice> choices,
        CompanionQuickAccessCommandIdentity? selected,
        bool showAll)
    {
        if (showAll)
        {
            return choices;
        }

        var recommended = choices
            .Where(IsRecommended)
            .ToList();

        // A custom or renamed action cannot be recognized from CSP's generic command
        // metadata. Keep an existing explicit selection visible even when it does not
        // match the recommendation heuristic.
        if (selected is not null)
        {
            var selectedChoice = choices.FirstOrDefault(choice =>
                SameIdentity(choice.Identity, selected));
            if (selectedChoice is not null &&
                recommended.All(choice => !SameIdentity(choice.Identity, selected)))
            {
                recommended.Add(selectedChoice);
            }
        }

        // Never strand a user who renamed both the action and its Quick Access set.
        // With no recognizable candidate, the full list is the only safe fallback.
        return recommended.Count == 0 ? choices : recommended;
    }

    internal static int RecommendedCount(
        IReadOnlyList<CompanionQuickAccessCommandChoice> choices) =>
        choices.Count(IsRecommended);

    private static bool IsRecommended(
        string displayName,
        string commandName,
        string setName)
    {
        var name = $"{displayName} {commandName} {setName}";
        if (name.Contains("CSP Palette Companion", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var english =
            name.Contains("copy", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("merged", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("visible", StringComparison.OrdinalIgnoreCase)) &&
            (name.Contains("selection", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("layer", StringComparison.OrdinalIgnoreCase));

        var german =
            name.Contains("kopier", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("sichtbar", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("zusammengef", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("kombin", StringComparison.OrdinalIgnoreCase)) &&
            (name.Contains("auswahl", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("ebene", StringComparison.OrdinalIgnoreCase));

        return english || german;
    }

    private static bool SameIdentity(
        CompanionQuickAccessCommandIdentity left,
        CompanionQuickAccessCommandIdentity right) =>
        string.Equals(left.CommandType, right.CommandType, StringComparison.Ordinal) &&
        string.Equals(left.CommandName, right.CommandName, StringComparison.Ordinal);
}
