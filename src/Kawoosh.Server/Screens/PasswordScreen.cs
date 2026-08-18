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
        // The player typed their password and pressed enter; the client never showed it, so
        // give echoing back before anything else is sent.
        context.Session.ShowInput();
        context.Switch(WorldScreen.ScreenName);
    }

    public void OnExit(ScreenContext context)
    {
    }
}
