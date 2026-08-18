using System.Text;

namespace Kawoosh.Server.Internal;

/// <summary>
/// Turns one .sgm file into key-value pairs. Pure: no filesystem, so every edge of the format
/// is testable without writing a file.
/// </summary>
internal static class MessageParser
{
    private const char CommentMarker = '#';
    private const char KeyValueSeparator = '=';
    private const string EscapedLineFeed = "\\n";

    /// <summary>
    /// Parses one file into <paramref name="messages" />, appending
    /// "&lt;file&gt;:&lt;line&gt;: &lt;message&gt;" strings to <paramref name="problems" />.
    /// The dictionary is passed in rather than returned so that a key repeated in a second
    /// file collides through the same path as one repeated in the same file.
    /// </summary>
    public static void Parse(
        string content,
        string sourceFile,
        IDictionary<string, string> messages,
        ICollection<string> problems
    )
    {
        var lines = NormalizeLines(content);

        for (var i = 0; i < lines.Count; i++)
        {
            ReadLine(lines[i], i + 1, sourceFile, messages, problems);
        }
    }

    private static void ReadLine(
        string line,
        int lineNumber,
        string sourceFile,
        IDictionary<string, string> messages,
        ICollection<string> problems
    )
    {
        var trimmed = line.TrimStart();

        if (trimmed.Length == 0 || trimmed[0] == CommentMarker)
        {
            return;
        }

        var separatorIndex = trimmed.IndexOf(KeyValueSeparator);

        if (separatorIndex < 0)
        {
            problems.Add($"{sourceFile}:{lineNumber}: expected 'key = value'");

            return;
        }

        var key = trimmed[..separatorIndex].Trim();

        if (key.Length == 0)
        {
            problems.Add($"{sourceFile}:{lineNumber}: message has no key");

            return;
        }

        // Only the leading whitespace goes: a prompt like "Password: " needs its trailing space.
        var value = trimmed[(separatorIndex + 1)..].TrimStart().Replace(EscapedLineFeed, "\n");

        if (!messages.TryAdd(key, value))
        {
            problems.Add($"{sourceFile}:{lineNumber}: duplicate message key '{key}', keeping the first");
        }
    }

    private static List<string> NormalizeLines(string content)
    {
        var text = content.Length > 0 && content[0] == '﻿' ? content[1..] : content;
        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                builder.Append('\n');

                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            builder.Append(text[i]);
        }

        return [.. builder.ToString().Split('\n')];
    }
}
