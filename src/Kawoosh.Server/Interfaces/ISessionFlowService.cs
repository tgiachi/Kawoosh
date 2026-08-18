using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// The conversation with one session: what a typed line means depends on where the session
/// is. Runs on the game loop's thread, so per-session state needs no lock.
/// </summary>
public interface ISessionFlowService
{
    /// <summary>Puts a freshly connected session at the first prompt.</summary>
    void Begin(TelnetSession session);

    /// <summary>Handles one line according to the session's current state.</summary>
    void Handle(TelnetSession session, string line);
}
