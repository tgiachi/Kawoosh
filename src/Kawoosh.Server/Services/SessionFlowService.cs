using Serilog;
using Kawoosh.Server.Data.World;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Types;

namespace Kawoosh.Server.Services;

/// <summary>
/// The login conversation and, once past it, the in-world command line.
/// </summary>
/// <remarks>
/// Any password is accepted, because there is no character store to check one against and no
/// outbound telnet negotiation to stop the client echoing it back. Both are needed before this
/// is a real gate; until then the step exists so the rest of the flow is built against its
/// shape rather than around its absence.
/// </remarks>
public sealed class SessionFlowService : ISessionFlowService
{
    private const int MinimumNameLength = 3;
    private const int MaximumNameLength = 16;

    private readonly ILogger _logger = Log.ForContext<SessionFlowService>();
    private readonly IMessageService _messages;

    public SessionFlowService(IMessageService messages)
    {
        _messages = messages;
    }

    public void Begin(TelnetSession session)
    {
        session.State = SessionState.AwaitingName;
        session.SendPrompt(_messages.Render("login.name-prompt"));
    }

    public void Handle(TelnetSession session, string line)
    {
        switch (session.State)
        {
            case SessionState.AwaitingName:
                HandleName(session, line.Trim());

                break;
            case SessionState.AwaitingPassword:
                HandlePassword(session);

                break;
            default:
                HandleInWorld(session, line.Trim());

                break;
        }
    }

    private void HandleName(TelnetSession session, string name)
    {
        var rejection = Reject(name);

        if (rejection is not null)
        {
            session.Send(_messages.Render(rejection));
            session.SendPrompt(_messages.Render("login.name-prompt"));

            return;
        }

        session.Character = new Character { Name = name };
        session.State = SessionState.AwaitingPassword;

        // Before the prompt, or the first characters of the password are already on screen by
        // the time the client stops echoing.
        session.HideInput();
        session.SendPrompt(_messages.Render("login.password-prompt"));
    }

    /// <summary>Returns the message key explaining why a name is unusable, or null.</summary>
    private static string? Reject(string name)
    {
        if (name.Length < MinimumNameLength)
        {
            return "login.name-too-short";
        }

        if (name.Length > MaximumNameLength)
        {
            return "login.name-too-long";
        }

        return name.All(char.IsLetter) ? null : "login.name-invalid";
    }

    private void HandlePassword(TelnetSession session)
    {
        // The player typed their password and pressed enter; the client never showed it, so
        // give echoing back before anything else is sent.
        session.ShowInput();

        // Whatever was typed is accepted; see the remarks on this class.
        var name = session.Character?.Name ?? string.Empty;

        session.State = SessionState.Playing;
        session.Send(_messages.Render("login.welcome", ("playerName", name)));

        _logger.Information("Session {SessionId} entered the world as {PlayerName}", session.Id, name);
    }

    private static void HandleInWorld(TelnetSession session, string line)
    {
        // Until there are real commands, saying it back is the honest placeholder.
        session.Send($"echo: {line}");
    }
}
