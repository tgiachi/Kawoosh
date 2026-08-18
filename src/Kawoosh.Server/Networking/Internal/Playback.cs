using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Text;

namespace Kawoosh.Server.Networking.Internal;

/// <summary>
/// What a session is in the middle of showing. Mutable and unsynchronised on purpose: it is
/// only ever read and written on the game loop's thread.
/// </summary>
internal sealed class Playback
{
    public TextScript Script { get; }
    public int NextIndex { get; set; }
    public long Handle { get; set; }
    public GameLoopCommand? Then { get; }

    public Playback(TextScript script, GameLoopCommand? then)
    {
        Script = script;
        Then = then;
    }
}
