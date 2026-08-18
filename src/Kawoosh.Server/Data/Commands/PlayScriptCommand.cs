using Kawoosh.Server.Data.Text;
using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// Sends one step of a script and schedules the next. Carrying the continuation means a
/// timed greeting can be followed by a prompt without the prompt racing it.
/// </summary>
public sealed record PlayScriptCommand(
    TelnetSession Session,
    TextScript Script,
    int Index,
    GameLoopCommand? Then
) : GameLoopCommand;
