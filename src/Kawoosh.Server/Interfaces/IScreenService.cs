using System.Diagnostics.CodeAnalysis;
using Kawoosh.Server.Data.Screens;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// Owns the screen directory and renders screens through variable substitution.
/// </summary>
public interface IScreenService
{
    /// <summary>Every loaded screen's art, by name.</summary>
    IReadOnlyCollection<string> ArtNames { get; }

    /// <summary>
    /// Reads every .sgs file in a directory, keyed by file name without extension.
    /// </summary>
    /// <param name="directoryPath">Directory holding the screens.</param>
    /// <param name="requiredScreens">Names that must be present; a missing one fails the load.</param>
    /// <exception cref="Kawoosh.Server.Exceptions.ContentLoadException">A file is malformed or a required screen is absent.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    void Load(string directoryPath, IReadOnlyCollection<string> requiredScreens);

    /// <summary>
    /// Re-reads the directory given to <see cref="Load" />. On failure the screens already
    /// being served are kept, so a typo saved on a live server blanks nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Load" /> has not run.</exception>
    void Reload();

    /// <summary>Finds a screen's art and metadata without rendering it.</summary>
    /// <param name="name">Screen name, matched without regard to case.</param>
    /// <param name="art">The art, when a .sgs is loaded under that name.</param>
    bool TryGetArt(string name, [MaybeNullWhen(false)] out ScreenArt art);

    /// <summary>
    /// Renders a screen, substituting variables now rather than at load, because a value
    /// like a player's name differs per session.
    /// </summary>
    /// <param name="name">Screen name, matched without regard to case.</param>
    /// <exception cref="KeyNotFoundException">No screen by that name is loaded.</exception>
    string Render(string name);
}
