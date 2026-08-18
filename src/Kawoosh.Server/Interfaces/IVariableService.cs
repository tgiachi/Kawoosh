namespace Kawoosh.Server.Interfaces;

/// <summary>
/// Substitutes <c>{name}</c> tokens in screens and messages.
/// </summary>
public interface IVariableService
{
    /// <summary>Registers a fixed value, replacing whatever was registered under this name.</summary>
    /// <param name="name">Token name, matched without regard to case.</param>
    /// <param name="value">Value rendered in place of the token.</param>
    void AddVariable(string name, object value);

    /// <summary>
    /// Registers a value computed at each substitution, replacing whatever was registered
    /// under this name. The builder runs on the thread doing the translation.
    /// </summary>
    /// <param name="name">Token name, matched without regard to case.</param>
    /// <param name="builder">Produces the value when the token is met.</param>
    void AddVariableBuilder(string name, Func<object> builder);

    /// <summary>Every registered name, sorted, for an admin listing.</summary>
    IReadOnlyList<string> GetVariableNames();

    /// <summary>
    /// Replaces every token in one pass. Substituted values are never re-examined, so a value
    /// that looks like a token is text and not a template.
    /// </summary>
    /// <param name="text">Text containing zero or more tokens.</param>
    string TranslateText(string text);
}
