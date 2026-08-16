using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Data.World;

public class SGWExit
{
    public SGWDirection Direction { get; set; }
    public int TargetVnum { get; set; }
    public SGWDoor? Door { get; set; }

    /// <summary>1-based line this exit was declared on, used to locate world-level diagnostics.</summary>
    public int SourceLine { get; set; }

    public override string ToString()
        => $"Exit({Direction} -> {TargetVnum}{(Door is null ? string.Empty : $", {Door}")})";
}
