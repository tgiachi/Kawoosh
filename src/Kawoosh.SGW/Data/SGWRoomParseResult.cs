using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Data;

/// <summary>
/// Outcome of a non-throwing room parse: the room when it is usable, plus every
/// diagnostic collected during the pass.
/// </summary>
public class SGWRoomParseResult
{
    public SGWRoom? Room { get; set; }
    public List<SGWDiagnostic> Diagnostics { get; set; } = [];

    public bool HasErrors
        => Diagnostics.Exists(d => d.Severity == SGWDiagnosticSeverity.Error);

    public IEnumerable<SGWDiagnostic> Errors
        => Diagnostics.Where(d => d.Severity == SGWDiagnosticSeverity.Error);

    public IEnumerable<SGWDiagnostic> Warnings
        => Diagnostics.Where(d => d.Severity == SGWDiagnosticSeverity.Warning);
}
