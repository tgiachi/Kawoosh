using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Data;

public class SGWExit
{
    public SGWDirection Direction { get; set; }
    public int TargetVnum { get; set; }
    public SGWDoor? Door { get; set; }

    public override string ToString()
        => $"Exit({Direction} -> {TargetVnum}{(Door is null ? string.Empty : $", {Door}")})";
}
