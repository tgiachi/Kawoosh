using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Screens;

/// <summary>
/// What every screen hook receives: the session it concerns, and the way to move on. Built
/// by the game loop for each hook call, so it is never shared between sessions. A screen
/// sends through <see cref="Session" />, which already knows how.
/// </summary>
public sealed class ScreenContext
{
    private readonly IGameLoopService _gameLoop;

    public TelnetSession Session { get; }

    public ScreenContext(IGameLoopService gameLoop, TelnetSession session)
    {
        _gameLoop = gameLoop;
        Session = session;
    }

    /// <summary>
    /// Queues a move to another screen. Queued rather than immediate: if this ran the hooks
    /// itself, a screen switching inside OnInput would re-enter the manager while OnInput was
    /// still on the stack. Queuing fixes the order — OnInput returns, then OnExit, then the
    /// art, then OnEnter.
    /// </summary>
    /// <param name="screenName">Where to go. An unknown name leaves the session where it is.</param>
    public void Switch(string screenName)
    {
        _gameLoop.Enqueue(new SwitchScreenCommand(Session, screenName));
    }
}
