using Kawoosh.SGW.Data.Diagnostic;
using Kawoosh.SGW.Data.World;

namespace Kawoosh.SGW.Interfaces;

public interface ISGWFileParser
{
    /// <summary>
    /// Parses a single room block and throws when any error diagnostic is produced.
    /// Content after the first '@end' is ignored.
    /// </summary>
    SGWRoom ParseRoom(string content);

    /// <summary>Parses a world file from disk.</summary>
    SGWWorld ParseWorld(string filePath);

    /// <summary>
    /// Parses a world file from disk without throwing, collecting every diagnostic.
    /// </summary>
    /// <param name="directoryPath">Directory path</param>
    /// <param name="diagnostics">A list to collect diagnostics.</param>
    /// <returns></returns>
    SGWWorld ParseWorldFromDirectory(string directoryPath, out List<SGWDiagnostic> diagnostics);

    /// <summary>
    /// Parses every room block in one file without throwing, collecting every diagnostic.
    /// </summary>
    /// <param name="content">The raw file text.</param>
    /// <param name="fileName">Name used as the "&lt;file&gt;" part of each diagnostic.</param>
    SGWFileParseResult TryParseFile(string content, string fileName);

    /// <summary>
    /// Parses a single room block without throwing, collecting every diagnostic.
    /// </summary>
    /// <param name="content">The raw room block text.</param>
    /// <param name="fileName">Name used as the "&lt;file&gt;" part of each diagnostic.</param>
    SGWRoomParseResult TryParseRoom(string content, string fileName);
}
