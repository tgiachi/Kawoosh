using Kawoosh.SGW.Data.Diagnostic;
using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Data.World;

/// <summary>
/// Outcome of parsing one .sgw file, which may hold any number of room blocks.
/// Rooms that produced an error are absent from <see cref="Rooms" /> but their
/// diagnostics are still reported.
/// </summary>
public class SGWFileParseResult
{
    public List<SGWRoom> Rooms { get; set; } = [];
    public List<SGWDiagnostic> Diagnostics { get; set; } = [];

    public bool HasErrors => Diagnostics.Exists(d => d.Severity == SGWDiagnosticSeverity.Error);

    public IEnumerable<SGWDiagnostic> Errors => Diagnostics.Where(d => d.Severity == SGWDiagnosticSeverity.Error);

    public IEnumerable<SGWDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == SGWDiagnosticSeverity.Warning);
}
