using System.Text;
using Kawoosh.Server.Data.Screens;

namespace Kawoosh.Server.Internal;

/// <summary>
/// Turns one .sgs file into a <see cref="Screen" />. Pure: no filesystem, so every edge of
/// the format is testable without writing a file.
/// </summary>
internal static class ScreenParser
{
    private const string Separator = "---";
    private const char DirectiveMarker = '@';

    /// <summary>
    /// Parses one screen, appending "&lt;file&gt;:&lt;line&gt;: &lt;message&gt;" strings to
    /// <paramref name="problems" />. Always returns a screen: the caller decides which
    /// problems are fatal.
    /// </summary>
    public static Screen Parse(string name, string content, string sourceFile, ICollection<string> problems)
    {
        var lines = NormalizeLines(content);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var bodyStart = 0;
        var reachedBody = true;

        if (OpensWithDirective(lines))
        {
            bodyStart = ReadMetadata(lines, sourceFile, metadata, problems, out reachedBody);
        }

        var body = JoinBody(lines, bodyStart);

        // Only complain about the body when there was one to read: an unclosed metadata block
        // has no body section, and reporting both would be one fault dressed as two.
        if (reachedBody && body.Trim().Length == 0)
        {
            problems.Add($"{sourceFile}:{bodyStart + 1}: screen body is empty");
        }

        return new Screen(name, body, metadata, sourceFile);
    }

    /// <summary>
    /// A metadata block exists only when the first non-blank line opens with '@'. Without
    /// this rule a screen starting with a "---" divider would lose its first line.
    /// </summary>
    private static bool OpensWithDirective(List<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            return line.TrimStart()[0] == DirectiveMarker;
        }

        return false;
    }

    /// <summary>
    /// Returns the index of the first body line. <paramref name="reachedBody" /> is false
    /// when the block was never closed, in which case there is no body to judge.
    /// </summary>
    private static int ReadMetadata(
        List<string> lines,
        string sourceFile,
        Dictionary<string, string> metadata,
        ICollection<string> problems,
        out bool reachedBody
    )
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            if (line == Separator)
            {
                reachedBody = true;

                return i + 1;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] != DirectiveMarker)
            {
                problems.Add($"{sourceFile}:{i + 1}: expected a '@key value' directive or the '---' separator");

                continue;
            }

            ReadDirective(line, i + 1, sourceFile, metadata, problems);
        }

        problems.Add($"{sourceFile}:{lines.Count}: metadata block is never closed by '---'");
        reachedBody = false;

        return lines.Count;
    }

    private static void ReadDirective(
        string line,
        int lineNumber,
        string sourceFile,
        Dictionary<string, string> metadata,
        ICollection<string> problems
    )
    {
        var separatorIndex = line.IndexOf(' ');
        var key = (separatorIndex < 0 ? line[1..] : line[1..separatorIndex]).ToLowerInvariant();
        var value = separatorIndex < 0 ? string.Empty : line[(separatorIndex + 1)..].Trim();

        if (key.Length == 0)
        {
            problems.Add($"{sourceFile}:{lineNumber}: directive has no name");

            return;
        }

        if (!metadata.TryAdd(key, value))
        {
            problems.Add($"{sourceFile}:{lineNumber}: metadata key '{key}' repeated, keeping the last value");
            metadata[key] = value;
        }
    }

    private static string JoinBody(List<string> lines, int bodyStart)
    {
        if (bodyStart >= lines.Count)
        {
            return string.Empty;
        }

        var body = string.Join("\n", lines.GetRange(bodyStart, lines.Count - bodyStart));

        // Editors add a final newline; the art did not ask for a blank last line.
        return body.EndsWith('\n') ? body[..^1] : body;
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
