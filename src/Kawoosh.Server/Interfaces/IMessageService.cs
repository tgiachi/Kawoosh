using System.Diagnostics.CodeAnalysis;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// Owns the message directory and renders one-line messages through variable substitution.
/// </summary>
public interface IMessageService
{
    /// <summary>Every loaded key.</summary>
    IReadOnlyCollection<string> MessageKeys { get; }

    /// <summary>Reads every .sgm file in a directory.</summary>
    /// <param name="directoryPath">Directory holding the messages.</param>
    /// <param name="requiredKeys">Keys that must be present; a missing one fails the load.</param>
    /// <exception cref="Kawoosh.Server.Exceptions.ContentLoadException">A file is malformed, a key is duplicated, or a required key is absent.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    void Load(string directoryPath, IReadOnlyCollection<string> requiredKeys);

    /// <summary>
    /// Re-reads the directory given to <see cref="Load" />. On failure the messages already
    /// being served are kept, so a typo saved on a live server blanks nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Load" /> has not run.</exception>
    void Reload();

    /// <summary>Returns the raw text, tokens and all, without substituting anything.</summary>
    /// <param name="key">Message key, matched without regard to case.</param>
    /// <param name="template">The raw text, when a message is loaded under that key.</param>
    bool TryGetTemplate(string key, [MaybeNullWhen(false)] out string template);

    /// <summary>Renders a message, substituting registered variables and the arguments given.</summary>
    /// <param name="key">Message key, matched without regard to case.</param>
    /// <param name="arguments">Values for this call only; they beat registered variables.</param>
    /// <exception cref="KeyNotFoundException">No message by that key is loaded.</exception>
    string Render(string key, params (string Name, object Value)[] arguments);
}
