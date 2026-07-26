using System.Collections.ObjectModel;
using System.Text.Json;

namespace CspPaletteCompanion.Companion;

/// <summary>
/// A command item registered in one of CLIP STUDIO's Quick Access sets.
/// The positional fields preserve the host's wire order for deterministic enumeration.
/// </summary>
public sealed record CompanionQuickAccessCommand(
    int SetIndex,
    string SetName,
    string SetUuid,
    int RowIndex,
    int ColumnIndex,
    int ItemIndex,
    string DisplayName,
    string CommandType,
    string CommandName,
    bool IsEnabled,
    bool IsChecked)
{
    /// <summary>
    /// The exact identity accepted by the companion protocol when this command is invoked.
    /// Display and set names are deliberately not part of the identity.
    /// </summary>
    public CompanionQuickAccessCommandIdentity Identity =>
        new(CommandType, CommandName);

    /// <summary>
    /// Returns metadata suitable for presenting this command as a user-selectable choice.
    /// </summary>
    public CompanionQuickAccessCommandChoice ToChoice() =>
        new(
            Identity,
            DisplayName,
            SetName,
            SetUuid,
            SetIndex,
            RowIndex,
            ColumnIndex,
            ItemIndex);
}

/// <summary>
/// Persistable identity of a Quick Access command. CLIP STUDIO invokes commands using
/// these two values; display names and Quick Access positions may change independently.
/// </summary>
public sealed record CompanionQuickAccessCommandIdentity(
    string CommandType,
    string CommandName);

/// <summary>
/// User-facing metadata for an enabled Quick Access command. The identity is the only
/// portion that should be persisted and later used for exact command invocation.
/// </summary>
public sealed record CompanionQuickAccessCommandChoice(
    CompanionQuickAccessCommandIdentity Identity,
    string DisplayName,
    string SetName,
    string SetUuid,
    int SetIndex,
    int RowIndex,
    int ColumnIndex,
    int ItemIndex);

/// <summary>
/// The command subset of CLIP STUDIO's Quick Access data and its current-set state.
/// Tool and drawing-color items are intentionally excluded.
/// </summary>
public sealed record CompanionQuickAccessData
{
    public required IReadOnlyList<CompanionQuickAccessCommand> Commands { get; init; }

    public required IReadOnlyList<CompanionQuickAccessCommand> EnabledCommands { get; init; }

    public IReadOnlyList<CompanionQuickAccessCommandChoice> EnabledCommandChoices =>
        new ReadOnlyCollection<CompanionQuickAccessCommandChoice>(
            EnabledCommands.Select(command => command.ToChoice()).ToList());

    public required int CurrentSetIndex { get; init; }

    public required int RemoteControllerSetIndex { get; init; }

    /// <summary>
    /// Finds a command using the exact case-sensitive identity used on the wire.
    /// The enabled requirement makes stale or currently unavailable stored choices fail closed.
    /// </summary>
    public CompanionQuickAccessCommand? FindEnabledCommand(
        CompanionQuickAccessCommandIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Commands.FirstOrDefault(command =>
            command.IsEnabled &&
            string.Equals(
                command.CommandType,
                identity.CommandType,
                StringComparison.Ordinal) &&
            string.Equals(
                command.CommandName,
                identity.CommandName,
                StringComparison.Ordinal));
    }

    internal static CompanionQuickAccessData Deserialize(CompanionFrame frame)
    {
        if (frame.Type != CompanionFrameType.Success)
        {
            throw new InvalidDataException("CLIP STUDIO rejected the Quick Access data request.");
        }

        if (frame.Detail is not JsonElement detail || detail.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Quick Access response has no object detail body.");
        }

        var commands = new List<CompanionQuickAccessCommand>();
        if (detail.TryGetProperty("ToolBarData", out var toolBarData))
        {
            if (toolBarData.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Quick Access ToolBarData must be an array.");
            }

            var setIndex = 0;
            foreach (var set in toolBarData.EnumerateArray())
            {
                ParseSet(set, setIndex, commands);
                setIndex++;
            }
        }

        var currentSetIndex = 0;
        var remoteControllerSetIndex = 0;
        if (detail.TryGetProperty("ToolBarViewInfo", out var viewInfo))
        {
            if (viewInfo.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Quick Access ToolBarViewInfo must be an object.");
            }

            currentSetIndex = ReadOptionalInt32(viewInfo, "ViewInfoCurrentSetIndex");
            remoteControllerSetIndex = ReadOptionalInt32(
                viewInfo,
                "ViewInfoRemoteControllerSetIndex");
        }

        var ordered = new ReadOnlyCollection<CompanionQuickAccessCommand>(commands);
        var enabled = new ReadOnlyCollection<CompanionQuickAccessCommand>(
            commands.Where(command => command.IsEnabled).ToList());
        return new CompanionQuickAccessData
        {
            Commands = ordered,
            EnabledCommands = enabled,
            CurrentSetIndex = currentSetIndex,
            RemoteControllerSetIndex = remoteControllerSetIndex,
        };
    }

    private static void ParseSet(
        JsonElement set,
        int setIndex,
        ICollection<CompanionQuickAccessCommand> destination)
    {
        if (set.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Each Quick Access set must be an object.");
        }

        var setName = ReadOptionalString(set, "ItemSetName");
        var setUuid = ReadOptionalString(set, "ItemSetUuid");
        if (!set.TryGetProperty("ItemSet", out var rows))
        {
            return;
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Quick Access ItemSet must be an array.");
        }

        var rowIndex = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Each Quick Access row must be an array.");
            }

            var columnIndex = 0;
            foreach (var column in row.EnumerateArray())
            {
                if (column.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("Each Quick Access column must be an array.");
                }

                var itemIndex = 0;
                foreach (var item in column.EnumerateArray())
                {
                    ParseCommandItem(
                        item,
                        setIndex,
                        setName,
                        setUuid,
                        rowIndex,
                        columnIndex,
                        itemIndex,
                        destination);
                    itemIndex++;
                }

                columnIndex++;
            }

            rowIndex++;
        }
    }

    private static void ParseCommandItem(
        JsonElement item,
        int setIndex,
        string setName,
        string setUuid,
        int rowIndex,
        int columnIndex,
        int itemIndex,
        ICollection<CompanionQuickAccessCommand> destination)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Each Quick Access item must be an object.");
        }

        if (!string.Equals(
                ReadOptionalString(item, "ItemIDType"),
                "Command",
                StringComparison.Ordinal))
        {
            return;
        }

        var commandType = ReadOptionalString(item, "ItemIDCommandType");
        var commandName = ReadOptionalString(item, "ItemIDCommandName");
        if (string.IsNullOrWhiteSpace(commandType) || string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        destination.Add(new CompanionQuickAccessCommand(
            setIndex,
            setName,
            setUuid,
            rowIndex,
            columnIndex,
            itemIndex,
            ReadOptionalString(item, "ItemShowName"),
            commandType,
            commandName,
            ReadOptionalBoolean(item, "ItemIsEnabled"),
            ReadOptionalBoolean(item, "ItemIsChecked")));
    }

    private static int ReadOptionalInt32(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var result))
        {
            throw new InvalidDataException($"Quick Access {propertyName} must be a 32-bit integer.");
        }

        return result;
    }

    private static string ReadOptionalString(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Quick Access {propertyName} must be a string.");
        }

        return property.GetString() ?? string.Empty;
    }

    private static bool ReadOptionalBoolean(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidDataException($"Quick Access {propertyName} must be a boolean.");
        }

        return property.GetBoolean();
    }
}
