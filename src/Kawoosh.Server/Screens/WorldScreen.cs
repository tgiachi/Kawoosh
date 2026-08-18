using Serilog;
using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Server.Screens;

/// <summary>
/// The game itself. OnInput is where real commands will go; until there are any, saying the
/// line back is the honest placeholder.
/// </summary>
public sealed class WorldScreen : IScreen
{
    public const string ScreenName = "world";

    private readonly ILogger _logger = Log.ForContext<WorldScreen>();
    private readonly IMessageService _messages;

    public string Name => ScreenName;

    public WorldScreen(IMessageService messages)
    {
        _messages = messages;
    }

    public void OnEnter(ScreenContext context)
    {
        var name = context.Session.Character?.Name ?? string.Empty;

        context.Session.Send(_messages.Render("login.welcome", ("playerName", name)));
        _logger.Information("Session {SessionId} entered the world as {PlayerName}", context.Session.Id, name);
    }

    public void OnInput(ScreenContext context, string line)
    {
        context.Session.Send($"echo: {line.Trim()}");
    }

    public void OnExit(ScreenContext context)
    {
    }
}
