using Kawoosh.SGW.Data;

namespace Kawoosh.SGW.Interfaces;

public interface ISGWFileParser
{
    /// <summary>Parses a world file from disk.</summary>
    SGWWorld ParseWorld(string filePath);

    /// <summary>
    /// Parses a single room block and throws when any error diagnostic is produced.
    /// </summary>
    SGWRoom ParseRoom(string content);

    /// <summary>
    /// Parses a single room block without throwing, collecting every diagnostic.
    /// </summary>
    /// <param name="content">The raw room block text.</param>
    /// <param name="fileName">Name used as the "&lt;file&gt;" part of each diagnostic.</param>
    SGWRoomParseResult TryParseRoom(string content, string fileName);


}
