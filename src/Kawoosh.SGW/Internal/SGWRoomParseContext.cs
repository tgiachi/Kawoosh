using Kawoosh.SGW.Data;
using Kawoosh.SGW.Data.Diagnostic;
using Kawoosh.SGW.Data.World;
using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Internal;

/// <summary>
/// Mutable state carried through a single room parse: the room being built, the
/// diagnostics collected so far, and the accumulators for free text and extras.
/// </summary>
internal class SGWRoomParseContext
{
    public string FileName { get; }

    public SGWRoom Room { get; set; } = new();
    public List<SGWDiagnostic> Diagnostics { get; set; } = [];
    public SGWRoomSection Section { get; set; } = SGWRoomSection.BeforeRoom;
    public int HeaderLine { get; set; }
    public bool SawSector { get; set; }
    public bool SawFlags { get; set; }
    public bool Closed { get; set; }

    public List<string> DescriptionLines { get; set; } = [];
    public HashSet<string> SeenKeywords { get; set; } = new(StringComparer.Ordinal);
    public HashSet<SGWDirection> SeenDirections { get; set; } = [];

    public List<string>? ExtraKeywords { get; set; }
    public string ExtraInline { get; set; } = string.Empty;
    public List<string> ExtraContinuations { get; set; } = [];
    public int ExtraLine { get; set; }

    public SGWRoomParseContext(string fileName)
    {
        FileName = fileName;
    }

    public void Report(SGWDiagnosticCode code, int line, string message)
    {
        Diagnostics.Add(new SGWDiagnostic(FileName, line, code, message));
    }

    public void ResetExtra()
    {
        ExtraKeywords = null;
        ExtraInline = string.Empty;
        ExtraContinuations = [];
    }
}
