using System.Globalization;
using System.Text;
using Kawoosh.Server.Data.Text;

namespace Kawoosh.Server.Internal;

/// <summary>
/// Turns text with <c>@delay</c> and <c>@typewriter</c> lines into timed steps. Pure: no
/// session and no clock, so every rule is testable without waiting for one.
/// </summary>
internal static class TextScriptCompiler
{
    private const string DelayDirective = "@delay ";
    private const string TypewriterDirective = "@typewriter ";

    public static TextScript Compile(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var steps = new List<TextStep>();
        var pending = new StringBuilder();
        var delay = 0;
        var perCharacter = 0;

        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (TryReadInt(line, DelayDirective, out var milliseconds))
            {
                // Flush first: a delay applies to what comes after it, not to what is already
                // written.
                delay = Flush(steps, pending, delay) + milliseconds;

                continue;
            }

            if (TryReadInt(line, TypewriterDirective, out var speed))
            {
                delay = Flush(steps, pending, delay);
                perCharacter = speed;

                continue;
            }

            if (perCharacter > 0)
            {
                delay = Flush(steps, pending, delay);
                AppendTypewritten(steps, line, delay, perCharacter);
                delay = 0;

                continue;
            }

            if (pending.Length > 0)
            {
                pending.Append('\n');
            }

            pending.Append(line);
        }

        Flush(steps, pending, delay);

        return new TextScript(steps);
    }

    /// <summary>Emits whatever text has accumulated, and returns the delay still owed.</summary>
    private static int Flush(List<TextStep> steps, StringBuilder pending, int delay)
    {
        if (pending.Length == 0)
        {
            return delay;
        }

        steps.Add(new TextStep(delay, pending.ToString(), true));
        pending.Clear();

        return 0;
    }

    private static void AppendTypewritten(List<TextStep> steps, string line, int delay, int perCharacter)
    {
        foreach (var character in line)
        {
            steps.Add(new TextStep(delay + perCharacter, character.ToString(), false));
            delay = 0;
        }

        // The line still has to end, and ending it costs no time.
        steps.Add(new TextStep(0, "", true));
    }

    /// <summary>
    /// Reads "@name &lt;non-negative int&gt;" from a line that starts at column zero. Anything
    /// else — an indent, a word instead of a number, a negative — is text, which keeps the
    /// format permissive and doubles as the escape.
    /// </summary>
    private static bool TryReadInt(string line, string directive, out int value)
    {
        value = 0;

        if (!line.StartsWith(directive, StringComparison.Ordinal))
        {
            return false;
        }

        var argument = line[directive.Length..].Trim();

        return int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
