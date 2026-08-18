using System.Diagnostics.CodeAnalysis;
using Kawoosh.Server.Data.Screens;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// Owns the screen directory and renders screens through variable substitution.
/// </summary>
public interface IScreenService
{
    /// <summary>Every loaded screen name.</summary>
    IReadOnlyCollection<string> ScreenNames { get; }

    /// <summary>
    /// Reads every .sgs file in a directory, keyed by file name without extension.
    /// </summary>
    /// <param name="directoryPath">Directory holding the screens.</param>
    /// <param name="requiredScreens">Names that must be present; a missing one fails the load.</param>
    /// <exception cref="Kawoosh.Server.Exceptions.ScreenLoadException">A file is malformed or a required screen is absent.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    void Load(string directoryPath, IReadOnlyCollection<string> requiredScreens);

    /// <summary>
    /// Re-reads the directory given to <see cref="Load" />. On failure the screens already
    /// being served are kept, so a typo saved on a live server blanks nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Load" /> has not run.</exception>
    void Reload();

    /// <summary>Finds a screen and its metadata without rendering it.</summary>
    /// <param name="name">Screen name, matched without regard to case.</param>
    /// <param name="screen">The screen, when one is loaded under that name.</param>
    bool TryGetScreen(string name, [MaybeNullWhen(false)] out Screen screen);

    /// <summary>
    /// Renders a screen, substituting variables now rather than at load, because a value
    /// like a player's name differs per session.
    /// </summary>
    /// <param name="name">Screen name, matched without regard to case.</param>
    /// <exception cref="KeyNotFoundException">No screen by that name is loaded.</exception>
    string Render(string name);
}
