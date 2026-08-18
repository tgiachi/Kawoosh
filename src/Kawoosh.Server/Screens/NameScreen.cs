using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Data.World;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Server.Screens;

/// <summary>
/// Where a player says who they are. Has no art of its own: a prompt is a message, not a
/// picture.
/// </summary>
public sealed class NameScreen : IScreen
{
    public const string ScreenName = "name";

    private const int MinimumNameLength = 3;
    private const int MaximumNameLength = 16;

    private readonly IMessageService _messages;

    public string Name => ScreenName;

    public NameScreen(IMessageService messages)
    {
        _messages = messages;
    }

    public void OnEnter(ScreenContext context)
    {
        context.Session.SendPrompt(_messages.Render("login.name-prompt"));
    }

    public void OnInput(ScreenContext context, string line)
    {
        var name = line.Trim();
        var rejection = Reject(name);

        if (rejection is not null)
        {
            context.Session.Send(_messages.Render(rejection));
            context.Session.SendPrompt(_messages.Render("login.name-prompt"));

            return;
        }

        context.Session.Character = new Character { Name = name };
        context.Switch(PasswordScreen.ScreenName);
    }

    public void OnExit(ScreenContext context)
    {
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
}
