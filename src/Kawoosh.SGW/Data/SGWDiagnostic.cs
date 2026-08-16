using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Data;

/// <summary>
/// A single parser diagnostic, rendered as "&lt;file&gt;:&lt;line&gt;: &lt;message&gt;"
/// per docs/sgw-format.md section 6.
/// </summary>
public class SGWDiagnostic
{
    public string File { get; set; }
    public int Line { get; set; }
    public SGWDiagnosticCode Code { get; set; }
    public SGWDiagnosticSeverity Severity { get; set; }
    public string Message { get; set; }

    /// <summary>
    /// The spec code as written in the catalogue, for example "E070" or "W001".
    /// Never printed as part of <see cref="Message"/>.
    /// </summary>
    public string CodeText
        => Severity == SGWDiagnosticSeverity.Warning
               ? $"W{(int)Code - 1000:D3}"
               : $"E{(int)Code:D3}";

    public SGWDiagnostic(string file, int line, SGWDiagnosticCode code, string message)
    {
        File = file;
        Line = line;
        Code = code;
        Message = message;
        Severity = code >= SGWDiagnosticCode.MissingDescription
                       ? SGWDiagnosticSeverity.Warning
                       : SGWDiagnosticSeverity.Error;
    }

    public override string ToString()
        => Severity == SGWDiagnosticSeverity.Warning
               ? $"{File}:{Line}: warning: {Message}"
               : $"{File}:{Line}: {Message}";
}
