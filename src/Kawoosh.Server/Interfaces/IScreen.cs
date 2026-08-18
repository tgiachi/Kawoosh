using Kawoosh.Server.Data.Screens;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// One screen a session can be on. A screen is the session's state: what it shows, and what
/// a typed line means while it is current. Its art, if it has any, is the .sgs file loaded
/// under the same <see cref="Name" />; a screen without one is not unusual, because a prompt
/// is a message rather than a picture.
/// </summary>
public interface IScreen
{
    /// <summary>The name this screen is switched to by, and the .sgs file shown for it.</summary>
    string Name { get; }

    /// <summary>
    /// Runs once the screen is fully shown, so any art has finished playing. A prompt belongs
    /// here rather than before the art, or the player is asked something over the top of it.
    /// </summary>
    void OnEnter(ScreenContext context);

    /// <summary>A line the player typed while this screen was current.</summary>
    void OnInput(ScreenContext context, string line);

    /// <summary>
    /// Runs before the session moves to another screen. Not guaranteed to be paired with a
    /// prior <see cref="OnEnter" /> — a switch away while this screen's own art is still
    /// playing, or a same-tick switch through and back past it, exits it without it ever
    /// having entered. A screen must not assume OnEnter ran before its OnExit does; anything
    /// acquired in one must be safe to release in the other even when the first never fired.
    /// </summary>
    void OnExit(ScreenContext context);
}
