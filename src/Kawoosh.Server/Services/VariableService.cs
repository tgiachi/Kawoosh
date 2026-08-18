using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Serilog;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Server.Services;

/// <summary>
/// Substitutes <c>{name}</c> tokens in screens and messages. A variable is always a function:
/// a static value is stored as one that returns it, so there is a single store and no
/// question of which kind wins when a name is registered twice.
/// </summary>
public sealed partial class VariableService : IVariableService
{
    // {{ and }} escape a literal brace, so ASCII art can contain them. A token name excludes
    // braces so an unclosed one cannot swallow the rest of the line.
    [GeneratedRegex(@"\{\{|\}\}|\{([^{}]+)\}")]
    private static partial Regex TokenRegex();

    private readonly ILogger _logger = Log.ForContext<VariableService>();

    private readonly ConcurrentDictionary<string, Func<object>> _variables =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _reportedUnknown =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a fixed value. Replaces whatever was registered under this name.</summary>
    public void AddVariable(string name, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        _variables[name] = () => value;
    }

    /// <summary>
    /// Registers a value computed at each substitution. Replaces whatever was registered
    /// under this name. The builder runs on the thread doing the translation, so it must be
    /// cheap; anything expensive caches on its own rather than being cached here behind the
    /// caller's back.
    /// </summary>
    public void AddVariableBuilder(string name, Func<object> builder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(builder);

        _variables[name] = builder;
    }

    /// <summary>Every registered name, sorted, for an admin listing.</summary>
    public IReadOnlyList<string> GetVariableNames()
    {
        return [.. _variables.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Replaces every token in one pass. Substituted values are never re-examined, so a value
    /// that happens to look like a token — a player's name, for instance — is text and not a
    /// template.
    /// </summary>
    public string TranslateText(string text, params (string Name, object Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(arguments);

        return TokenRegex().Replace(text, match => Resolve(match, arguments));
    }

    private string Resolve(Match match, (string Name, object Value)[] arguments)
    {
        // No capture means the match was {{ or }}: emit the single brace it stands for.
        if (!match.Groups[1].Success)
        {
            return match.Value[..1];
        }

        var name = match.Groups[1].Value;

        // A short linear scan: a message carries a handful of arguments, and this avoids a
        // dictionary allocation per rendered line.
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return Format(argument.Value);
            }
        }

        if (!_variables.TryGetValue(name, out var builder))
        {
            ReportUnknown(name);

            return string.Empty;
        }

        try
        {
            return Format(builder());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Variable {VariableName} failed to build, rendered as empty", name);

            return string.Empty;
        }
    }

    /// <summary>Invariant culture: the same text wherever the server runs.</summary>
    private static string Format(object value)
    {
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void ReportUnknown(string name)
    {
        // Once per name: a missing variable in a screen would otherwise log on every view.
        if (_reportedUnknown.TryAdd(name, 0))
        {
            _logger.Warning("Unknown variable {VariableName}, rendered as empty", name);
        }
    }
}
