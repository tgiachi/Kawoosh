namespace Kawoosh.SGW.Data.World;

/// <summary>
/// A loaded world: every room that parsed cleanly, keyed by vnum.
/// </summary>
public class SGWWorld
{
    private readonly Dictionary<int, SGWRoom> _rooms = new();

    public IReadOnlyDictionary<int, SGWRoom> Rooms => _rooms;

    public int Count => _rooms.Count;

    /// <summary>
    /// Adds a room. Returns false when a room with the same vnum is already present,
    /// leaving the existing one untouched so the caller can report the duplicate.
    /// </summary>
    public bool AddRoom(SGWRoom room)
    {
        return _rooms.TryAdd(room.Id, room);
    }

    public SGWRoom? GetRoom(int vnum)
    {
        return _rooms.GetValueOrDefault(vnum);
    }

    public bool Contains(int vnum)
    {
        return _rooms.ContainsKey(vnum);
    }

    public override string ToString()
        => $"World({Count} rooms)";
}
