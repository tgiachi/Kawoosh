using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Server.Screens;

/// <summary>
/// The banner every client sees on connect. Its art is greeting.sgs, played by the loop
/// before OnEnter runs, which is why moving straight on does not cut it short.
/// </summary>
public sealed class GreetingScreen : IScreen
{
    public const string ScreenName = "greeting";

    public string Name => ScreenName;

    public void OnEnter(ScreenContext context)
    {
        context.Switch(NameScreen.ScreenName);
    }

    public void OnInput(ScreenContext context, string line)
    {
        // Nothing. The switch queued in OnEnter lands within a tick, and a line typed in that
        // window was aimed at the banner, not at the game.
    }

    public void OnExit(ScreenContext context)
    {
    }
}
