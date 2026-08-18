using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Server.Screens;

/// <summary>
/// Where a password is taken. Any password is accepted, because there is no character store
/// to check one against; the step exists so the rest of the conversation is built against its
/// shape rather than around its absence.
/// </summary>
public sealed class PasswordScreen : IScreen
{
    public const string ScreenName = "password";

    private readonly IMessageService _messages;

    public string Name => ScreenName;

    public PasswordScreen(IMessageService messages)
    {
        _messages = messages;
    }

    public void OnEnter(ScreenContext context)
    {
        // Before the prompt, or the first characters of the password are already on screen by
        // the time the client stops echoing.
        context.Session.HideInput();
        context.Session.SendPrompt(_messages.Render("login.password-prompt"));
    }

    public void OnInput(ScreenContext context, string line)
    {
        // Echoing comes back in OnExit instead of here: OnExit runs unconditionally on the
        // way out of this screen no matter which exit gets the session there, so restoring it
        // from that one place is both simpler and correct for every path, not just this one.
        context.Switch(WorldScreen.ScreenName);
    }

    public void OnExit(ScreenContext context)
    {
        // Undoes OnEnter's HideInput regardless of how the screen is left — a timeout, a
        // kick, or a forced switch all reach here without ever calling OnInput. Safe even
        // when HideInput never ran: it queues IAC WONT ECHO, which a client that is already
        // echoing ignores.
        context.Session.ShowInput();
    }
}
