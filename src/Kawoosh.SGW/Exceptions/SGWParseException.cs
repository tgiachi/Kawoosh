using Kawoosh.SGW.Data.Diagnostic;

namespace Kawoosh.SGW.Exceptions;

/// <summary>
/// Thrown by the throwing parser entry points when at least one error diagnostic was
/// collected. Warnings alone never raise this.
/// </summary>
public class SGWParseException : Exception
{
    public IReadOnlyList<SGWDiagnostic> Diagnostics { get; }

    public string FilePath { get; }

    public SGWParseException(IReadOnlyList<SGWDiagnostic> diagnostics, string filePath = null)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
        FilePath = filePath;
    }

    private static string BuildMessage(IReadOnlyList<SGWDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));
}
