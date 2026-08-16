using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Internal;

/// <summary>
/// Canonical token tables from docs/sgw-format.md sections 3, 4.1 and 4.2.
/// Keys are the lowercased canonical names plus every accepted alias.
/// </summary>
internal static class SGWTokenTables
{
    public static readonly IReadOnlyDictionary<string, SGWDirection> Directions =
        new Dictionary<string, SGWDirection>(StringComparer.Ordinal)
        {
            ["north"] = SGWDirection.North,
            ["n"] = SGWDirection.North,
            ["south"] = SGWDirection.South,
            ["s"] = SGWDirection.South,
            ["east"] = SGWDirection.East,
            ["e"] = SGWDirection.East,
            ["west"] = SGWDirection.West,
            ["w"] = SGWDirection.West,
            ["up"] = SGWDirection.Up,
            ["u"] = SGWDirection.Up,
            ["down"] = SGWDirection.Down,
            ["d"] = SGWDirection.Down
        };

    public static readonly IReadOnlyDictionary<string, string> Sectors =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["indoor"] = "indoor",
            ["inside"] = "indoor",
            ["city"] = "city",
            ["urban"] = "city",
            ["field"] = "field",
            ["plain"] = "field",
            ["forest"] = "forest",
            ["wood"] = "forest",
            ["hills"] = "hills",
            ["hill"] = "hills",
            ["mountain"] = "mountain",
            ["mountains"] = "mountain",
            ["desert"] = "desert",
            ["road"] = "road",
            ["path"] = "road",
            ["swamp"] = "swamp",
            ["marsh"] = "swamp",
            ["cave"] = "cave",
            ["cavern"] = "cave",
            ["dungeon"] = "dungeon",
            ["water_swim"] = "water_swim",
            ["water"] = "water_swim",
            ["water_noswim"] = "water_noswim",
            ["deepwater"] = "water_noswim",
            ["underwater"] = "underwater",
            ["submerged"] = "underwater",
            ["air"] = "air",
            ["sky"] = "air"
        };

    public static readonly IReadOnlyDictionary<string, RoomFlag> Flags =
        new Dictionary<string, RoomFlag>(StringComparer.Ordinal)
        {
            ["dark"] = RoomFlag.Dark,
            ["no_mob"] = RoomFlag.NoMob,
            ["nomob"] = RoomFlag.NoMob,
            ["indoor"] = RoomFlag.Indoors,
            ["indoors"] = RoomFlag.Indoors,
            ["safe"] = RoomFlag.Safe,
            ["private"] = RoomFlag.Private,
            ["solitary"] = RoomFlag.Solitary,
            ["no_recall"] = RoomFlag.NoRecall,
            ["norecall"] = RoomFlag.NoRecall
        };

    public static readonly IReadOnlySet<string> ScriptEvents =
        new HashSet<string>(StringComparer.Ordinal) { "enter", "exit", "look" };

    public static string DirectionName(SGWDirection direction)
    {
        return direction.ToString().ToLowerInvariant();
    }
}
