using System.Diagnostics.CodeAnalysis;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// Every screen the server knows, by name. Resolution only: moving a session between screens
/// is the game loop's job, because the loop is the one thread that owns session state.
/// </summary>
public interface IScreenManager
{
    /// <summary>Every registered screen name.</summary>
    IReadOnlyCollection<string> ScreenNames { get; }

    /// <summary>Finds a screen by name, matched without regard to case.</summary>
    /// <param name="name">The screen name. An empty name never resolves.</param>
    /// <param name="screen">The screen, when one is registered under that name.</param>
    bool TryGetScreen(string name, [MaybeNullWhen(false)] out IScreen screen);
}
