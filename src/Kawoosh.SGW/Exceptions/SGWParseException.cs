using Kawoosh.SGW.Data;

namespace Kawoosh.SGW.Exceptions;

/// <summary>
/// Thrown by the throwing parser entry points when at least one error diagnostic was
/// collected. Warnings alone never raise this.
/// </summary>
public class SGWParseException : Exception
{
    public IReadOnlyList<SGWDiagnostic> Diagnostics { get; }

    public SGWParseException(IReadOnlyList<SGWDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    private static string BuildMessage(IReadOnlyList<SGWDiagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));
    }
}
