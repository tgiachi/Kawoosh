using System.Text;
using Serilog;
using Kawoosh.SGW.Data;
using Kawoosh.SGW.Exceptions;
using Kawoosh.SGW.Interfaces;
using Kawoosh.SGW.Internal;
using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Services;

public class SGWFileParser : ISGWFileParser
{
    private const string InMemoryFileName = "<memory>";
    private const int MinVnum = 1;
    private const int MaxVnum = 2147483647;

    private readonly ILogger _logger = Log.ForContext<SGWFileParser>();

    public SGWWorld ParseWorld(string filePath)
    {
        var world = new SGWWorld();

        try
        {
            if (!File.Exists(filePath))
            {
                _logger.Error("World file not found: {FilePath}", filePath);

                throw new FileNotFoundException($"World file not found: {filePath}");
            }

            using var reader = new StringReader(File.ReadAllText(filePath));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error parsing world file: {FilePath}", filePath);

            throw;
        }

        return world;
    }

    public SGWRoom ParseRoom(string content)
    {
        var result = TryParseRoom(content, InMemoryFileName);

        return result.HasErrors ? throw new SGWParseException(result.Diagnostics) : result.Room!;
    }

    public SGWRoomParseResult TryParseRoom(string content, string fileName)
    {
        var context = new SGWRoomParseContext(fileName);
        var lines = NormalizeLines(content);

        for (var i = 0; i < lines.Count && !context.Closed; i++)
        {
            ParseLine(lines[i], i + 1, context);
        }

        return Finish(context);
    }



    // ------------------------------------------------------------- line loop

    private static void ParseLine(string raw, int lineNumber, SGWRoomParseContext context)
    {
        var firstIndex = FirstNonWhitespaceIndex(raw);
        var isBlank = firstIndex < 0;
        var isEscaped = !isBlank && raw[firstIndex] == '\\';

        // Comments are recognised everywhere, including inside free text (spec 1.6).
        if (!isBlank && !isEscaped && raw[firstIndex] == '#')
        {
            return;
        }

        var literal = isEscaped ? raw.Remove(firstIndex, 1) : raw;
        var trimmed = isBlank ? string.Empty : raw[firstIndex..];

        // An open extra block swallows blank lines and every indented line (spec 2.6).
        if (context.ExtraKeywords is not null)
        {
            if (isBlank)
            {
                context.ExtraContinuations.Add(string.Empty);

                return;
            }

            if (char.IsWhiteSpace(raw[0]))
            {
                context.ExtraContinuations.Add(literal);

                return;
            }

            FlushExtra(context);
        }

        if (context.Section == SGWRoomSection.BeforeRoom)
        {
            ParseLineBeforeRoom(trimmed, isBlank, isEscaped, lineNumber, context);

            return;
        }

        if (isBlank)
        {
            if (context.Section == SGWRoomSection.Description)
            {
                context.DescriptionLines.Add(string.Empty);
            }

            return;
        }

        if (isEscaped)
        {
            if (context.Section == SGWRoomSection.Elements)
            {
                context.Report(
                    SGWDiagnosticCode.UnexpectedTextOutsideRoom,
                    lineNumber,
                    "unexpected text outside of a room block"
                );

                return;
            }

            context.Section = SGWRoomSection.Description;
            context.DescriptionLines.Add(literal);

            return;
        }

        ParseLineInsideRoom(trimmed, literal, lineNumber, context);
    }

    private static void ParseLineBeforeRoom(
        string trimmed,
        bool isBlank,
        bool isEscaped,
        int lineNumber,
        SGWRoomParseContext context
    )
    {
        if (isBlank)
        {
            return;
        }

        if (!isEscaped && FirstToken(trimmed).Equals("@room", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHeader(trimmed, lineNumber, context))
            {
                context.HeaderLine = lineNumber;
                context.Section = SGWRoomSection.Attributes;
            }

            return;
        }

        if (!isEscaped && FirstToken(trimmed).Equals("@end", StringComparison.OrdinalIgnoreCase))
        {
            context.Report(SGWDiagnosticCode.EndWithoutRoom, lineNumber, "'@end' without a matching '@room'");

            return;
        }

        context.Report(
            SGWDiagnosticCode.UnexpectedTextOutsideRoom,
            lineNumber,
            "unexpected text outside of a room block"
        );
    }

    private static void ParseLineInsideRoom(
        string trimmed,
        string literal,
        int lineNumber,
        SGWRoomParseContext context
    )
    {
        var token = FirstToken(trimmed);

        if (token.Equals("@end", StringComparison.OrdinalIgnoreCase))
        {
            FlushExtra(context);
            context.Closed = true;

            return;
        }

        if (token.Equals("@room", StringComparison.OrdinalIgnoreCase))
        {
            context.Report(SGWDiagnosticCode.RoomInsideRoom, lineNumber, "'@room' inside an unterminated room block");

            return;
        }

        if (token.Equals("@script", StringComparison.OrdinalIgnoreCase))
        {
            context.Section = SGWRoomSection.Elements;
            ParseScriptLine(trimmed, lineNumber, context);

            return;
        }

        if (trimmed[0] == '>')
        {
            context.Section = SGWRoomSection.Elements;
            ParseExitLine(trimmed, lineNumber, context);

            return;
        }

        if (IsExtraOpener(trimmed))
        {
            context.Section = SGWRoomSection.Elements;
            ParseExtraOpener(trimmed, lineNumber, context);

            return;
        }

        if (trimmed[0] == '@')
        {
            context.Report(SGWDiagnosticCode.UnknownDirective, lineNumber, $"unknown directive '{token}'");

            return;
        }

        if (context.Section == SGWRoomSection.Elements)
        {
            context.Report(
                SGWDiagnosticCode.UnexpectedTextOutsideRoom,
                lineNumber,
                "unexpected text outside of a room block"
            );

            return;
        }

        if (context.Section == SGWRoomSection.Attributes && TrySplitAttribute(trimmed, out var key, out var value))
        {
            ParseAttribute(key, value, lineNumber, context);

            return;
        }

        context.Section = SGWRoomSection.Description;
        context.DescriptionLines.Add(literal);
    }

    private static SGWRoomParseResult Finish(SGWRoomParseContext context)
    {
        var result = new SGWRoomParseResult { Diagnostics = context.Diagnostics };

        FlushExtra(context);

        if (context.HeaderLine == 0)
        {
            return result;
        }

        if (!context.Closed)
        {
            context.Report(
                SGWDiagnosticCode.UnterminatedRoomBlock,
                context.HeaderLine,
                "unterminated room block, expected '@end' before end of file"
            );
        }

        context.Room.Description = AssembleDescription(context.DescriptionLines);

        if (!context.SawSector)
        {
            context.Report(
                SGWDiagnosticCode.MissingSector,
                context.HeaderLine,
                $"room {context.Room.Id} is missing the required 'sector' attribute"
            );
        }

        if (context.Room.Description.Length == 0)
        {
            context.Report(
                SGWDiagnosticCode.MissingDescription,
                context.HeaderLine,
                $"room {context.Room.Id} has no description"
            );
        }

        // A room that produced any error is excluded from the world (spec 5.5 rule 27).
        result.Room = result.HasErrors ? null : context.Room;

        return result;
    }

    // ------------------------------------------------------------- attributes

    private static void ParseAttribute(string key, string value, int lineNumber, SGWRoomParseContext context)
    {
        var lowerKey = key.ToLowerInvariant();

        if (lowerKey != "sector" && lowerKey != "flags")
        {
            context.Report(SGWDiagnosticCode.UnknownAttribute, lineNumber, $"unknown attribute '{key}'");

            return;
        }

        if ((lowerKey == "sector" && context.SawSector) || (lowerKey == "flags" && context.SawFlags))
        {
            context.Report(SGWDiagnosticCode.DuplicateAttribute, lineNumber, $"duplicate '{lowerKey}' attribute");

            return;
        }

        var trimmedValue = value.Trim();

        if (trimmedValue.Length == 0)
        {
            context.Report(
                SGWDiagnosticCode.EmptyAttributeValue,
                lineNumber,
                $"attribute '{lowerKey}' has an empty value"
            );

            return;
        }

        if (lowerKey == "sector")
        {
            context.SawSector = true;
            ParseSector(trimmedValue, lineNumber, context);

            return;
        }

        context.SawFlags = true;
        ParseFlags(trimmedValue, lineNumber, context);
    }

    private static void ParseSector(string value, int lineNumber, SGWRoomParseContext context)
    {
        var tokens = value.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length > 1)
        {
            context.Report(
                SGWDiagnosticCode.SectorExpectsSingleValue,
                lineNumber,
                $"sector expects a single value, found {tokens.Length}"
            );

            return;
        }

        if (!SGWTokenTables.Sectors.TryGetValue(tokens[0].ToLowerInvariant(), out var canonical))
        {
            context.Report(SGWDiagnosticCode.UnknownSector, lineNumber, $"unknown sector '{tokens[0]}'");

            return;
        }

        context.Room.Sector = canonical;
    }

    private static void ParseFlags(string value, int lineNumber, SGWRoomParseContext context)
    {
        var tokens = value.Split((char[])[' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        var flags = new List<RoomFlag>();
        var hasNone = false;

        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();

            if (lower == "none")
            {
                if (hasNone)
                {
                    context.Report(SGWDiagnosticCode.DuplicateRoomFlag, lineNumber, $"duplicate room flag '{token}'");

                    continue;
                }

                hasNone = true;

                continue;
            }

            if (!SGWTokenTables.Flags.TryGetValue(lower, out var flag))
            {
                context.Report(SGWDiagnosticCode.UnknownRoomFlag, lineNumber, $"unknown room flag '{token}'");

                continue;
            }

            if (flags.Contains(flag))
            {
                context.Report(SGWDiagnosticCode.DuplicateRoomFlag, lineNumber, $"duplicate room flag '{token}'");

                continue;
            }

            flags.Add(flag);
        }

        if (hasNone && flags.Count > 0)
        {
            context.Report(
                SGWDiagnosticCode.NoneFlagCombined,
                lineNumber,
                "flag 'none' cannot be combined with other flags"
            );

            return;
        }

        context.Room.Flags = flags;
    }

    // ----------------------------------------------------------------- header

    private static bool TryParseHeader(string trimmed, int lineNumber, SGWRoomParseContext context)
    {
        var position = SkipWhitespace(trimmed, "@room".Length);

        if (!TryReadVnum(trimmed, ref position, lineNumber, context, out var vnum))
        {
            return false;
        }

        position = SkipWhitespace(trimmed, position);

        if (!TryReadQuotedString(trimmed, ref position, lineNumber, context, out var name))
        {
            return false;
        }

        if (SkipWhitespace(trimmed, position) < trimmed.Length)
        {
            context.Report(SGWDiagnosticCode.TextAfterRoomName, lineNumber, "unexpected text after room name");

            return false;
        }

        context.Room.Id = vnum;
        context.Room.Name = name;

        return true;
    }

    // ------------------------------------------------------------------ exits

    private static void ParseExitLine(string trimmed, int lineNumber, SGWRoomParseContext context)
    {
        var position = SkipWhitespace(trimmed, 1);
        var directionToken = ReadToken(trimmed, ref position);

        if (!SGWTokenTables.Directions.TryGetValue(directionToken.ToLowerInvariant(), out var direction))
        {
            context.Report(SGWDiagnosticCode.UnknownDirection, lineNumber, $"unknown direction '{directionToken}'");

            return;
        }

        position = SkipWhitespace(trimmed, position);

        if (!TryReadVnum(trimmed, ref position, lineNumber, context, out var target))
        {
            return;
        }

        var exit = new SGWExit { Direction = direction, TargetVnum = target };

        position = SkipWhitespace(trimmed, position);

        if (position < trimmed.Length && !TryParseDoorSpec(trimmed, ref position, lineNumber, exit, context))
        {
            return;
        }

        if (!context.SeenDirections.Add(direction))
        {
            context.Report(
                SGWDiagnosticCode.DuplicateExit,
                lineNumber,
                $"duplicate exit '{SGWTokenTables.DirectionName(direction)}' in room {context.Room.Id}"
            );

            return;
        }

        context.Room.Exits.Add(exit);
    }

    private static bool TryParseDoorSpec(
        string trimmed,
        ref int position,
        int lineNumber,
        SGWExit exit,
        SGWRoomParseContext context
    )
    {
        var keyword = ReadToken(trimmed, ref position);

        if (keyword.Equals("locked", StringComparison.OrdinalIgnoreCase) ||
            keyword.StartsWith("key", StringComparison.OrdinalIgnoreCase))
        {
            var name = keyword.StartsWith("key", StringComparison.OrdinalIgnoreCase) ? "key" : "locked";
            context.Report(
                SGWDiagnosticCode.DoorModifierWithoutDoor,
                lineNumber,
                $"'{name}' requires a 'door' specification"
            );

            return false;
        }

        if (!keyword.Equals("door", StringComparison.OrdinalIgnoreCase))
        {
            context.Report(
                SGWDiagnosticCode.UnexpectedTextAfterExit,
                lineNumber,
                "unexpected text after exit definition"
            );

            return false;
        }

        position = SkipWhitespace(trimmed, position);

        if (!TryReadQuotedString(trimmed, ref position, lineNumber, context, out var doorName))
        {
            return false;
        }

        var door = new SGWDoor { Name = doorName };

        while (true)
        {
            position = SkipWhitespace(trimmed, position);

            if (position >= trimmed.Length)
            {
                break;
            }

            if (!TryParseDoorModifier(ReadToken(trimmed, ref position), lineNumber, door, context))
            {
                return false;
            }
        }

        exit.Door = door;

        return true;
    }

    private static bool TryParseDoorModifier(
        string modifier,
        int lineNumber,
        SGWDoor door,
        SGWRoomParseContext context
    )
    {
        if (modifier.Equals("locked", StringComparison.OrdinalIgnoreCase))
        {
            if (door.IsLocked)
            {
                context.Report(SGWDiagnosticCode.DuplicateDoorModifier, lineNumber, "duplicate door modifier 'locked'");

                return false;
            }

            door.IsLocked = true;

            return true;
        }

        if (!modifier.StartsWith("key", StringComparison.OrdinalIgnoreCase))
        {
            context.Report(
                SGWDiagnosticCode.UnexpectedTextAfterExit,
                lineNumber,
                "unexpected text after exit definition"
            );

            return false;
        }

        if (modifier.Length <= "key=".Length || modifier["key".Length] != '=')
        {
            context.Report(
                SGWDiagnosticCode.MalformedKeyModifier,
                lineNumber,
                "'key' expects the form key=<vnum> with no spaces"
            );

            return false;
        }

        if (door.KeyVnum.HasValue)
        {
            context.Report(SGWDiagnosticCode.DuplicateDoorModifier, lineNumber, "duplicate door modifier 'key'");

            return false;
        }

        var keyPosition = 0;

        if (!TryReadVnum(modifier["key=".Length..], ref keyPosition, lineNumber, context, out var keyVnum))
        {
            return false;
        }

        door.KeyVnum = keyVnum;

        return true;
    }

    // ---------------------------------------------------------------- scripts

    private static void ParseScriptLine(string trimmed, int lineNumber, SGWRoomParseContext context)
    {
        var position = SkipWhitespace(trimmed, "@script".Length);
        var eventToken = ReadToken(trimmed, ref position);
        var eventName = eventToken.ToLowerInvariant();

        if (!SGWTokenTables.ScriptEvents.Contains(eventName))
        {
            context.Report(SGWDiagnosticCode.UnknownScriptEvent, lineNumber, $"unknown script event '{eventToken}'");

            return;
        }

        position = SkipWhitespace(trimmed, position);
        var scriptName = ReadToken(trimmed, ref position);

        if (!IsQualifiedName(scriptName))
        {
            context.Report(
                SGWDiagnosticCode.NotFullyQualifiedScriptName,
                lineNumber,
                $"'{scriptName}' is not a fully qualified script name"
            );

            return;
        }

        if (SkipWhitespace(trimmed, position) < trimmed.Length)
        {
            context.Report(SGWDiagnosticCode.TextAfterScriptName, lineNumber, "unexpected text after script name");

            return;
        }

        if (context.Room.Scripts.ContainsKey(eventName))
        {
            context.Report(
                SGWDiagnosticCode.DuplicateScriptEvent,
                lineNumber,
                $"duplicate script event '{eventName}' in room {context.Room.Id}"
            );

            return;
        }

        context.Room.AddScript(eventName, scriptName);
    }

    // ----------------------------------------------------------------- extras

    private static bool IsExtraOpener(string trimmed)
    {
        if (!trimmed.StartsWith("extra", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = "extra".Length;

        if (position >= trimmed.Length || !char.IsWhiteSpace(trimmed[position]))
        {
            return false;
        }

        position = SkipWhitespace(trimmed, position);

        // The grammar requires a quoted keyword list; without it the line is prose.
        return position < trimmed.Length && trimmed[position] == '"';
    }

    private static void ParseExtraOpener(string trimmed, int lineNumber, SGWRoomParseContext context)
    {
        context.ResetExtra();
        context.ExtraLine = lineNumber;

        var position = SkipWhitespace(trimmed, "extra".Length);

        if (!TryReadQuotedString(trimmed, ref position, lineNumber, context, out var keywordList))
        {
            return;
        }

        position = SkipWhitespace(trimmed, position);

        if (position >= trimmed.Length || trimmed[position] != ':')
        {
            context.Report(
                SGWDiagnosticCode.MalformedExtraHeader,
                lineNumber,
                "malformed extra description header, expected ':' after the keyword list"
            );

            return;
        }

        var keywords = keywordList
                       .Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                       .Select(k => k.ToLowerInvariant())
                       .ToList();

        if (keywords.Count == 0)
        {
            context.Report(
                SGWDiagnosticCode.EmptyExtraKeywordList,
                lineNumber,
                "extra description has an empty keyword list"
            );

            return;
        }

        foreach (var keyword in keywords.Where(keyword => !context.SeenKeywords.Add(keyword)))
        {
            context.Report(
                SGWDiagnosticCode.DuplicateExtraKeyword,
                lineNumber,
                $"duplicate extra keyword '{keyword}' in room {context.Room.Id}"
            );

            return;
        }

        context.ExtraInline = trimmed[(position + 1)..];
        context.ExtraKeywords = keywords;
    }

    private static void FlushExtra(SGWRoomParseContext context)
    {
        if (context.ExtraKeywords is null)
        {
            return;
        }

        var text = AssembleExtraText(context.ExtraInline, context.ExtraContinuations);

        if (text.Length == 0)
        {
            var label = context.ExtraKeywords.Count > 0 ? context.ExtraKeywords[0] : string.Empty;
            context.Report(
                SGWDiagnosticCode.EmptyExtraText,
                context.ExtraLine,
                $"extra description '{label}' has no text"
            );
        }
        else
        {
            context.Room.Extras.Add(new SGWExtraDescription { Keywords = context.ExtraKeywords, Text = text });
        }

        context.ResetExtra();
    }

    // --------------------------------------------------------- text assembly

    private static string AssembleDescription(List<string> lines)
    {
        return JoinTrimmedBlock(lines.Select(l => l.TrimEnd()).ToList());
    }

    private static string AssembleExtraText(string inlineText, List<string> continuations)
    {
        var lines = new List<string>();
        var inline = inlineText.Trim();

        if (inline.Length > 0)
        {
            lines.Add(inline);
        }

        var prefix = LongestCommonWhitespacePrefix(continuations);

        foreach (var continuation in continuations)
        {
            if (continuation.Trim().Length == 0)
            {
                lines.Add(string.Empty);

                continue;
            }

            lines.Add(continuation[prefix..].TrimEnd());
        }

        return JoinTrimmedBlock(lines);
    }

    private static int LongestCommonWhitespacePrefix(List<string> lines)
    {
        string? prefix = null;

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var indent = line[..FirstNonWhitespaceIndex(line)];

            if (prefix is null)
            {
                prefix = indent;

                continue;
            }

            var common = 0;

            while (common < prefix.Length && common < indent.Length && prefix[common] == indent[common])
            {
                common++;
            }

            prefix = prefix[..common];
        }

        return prefix?.Length ?? 0;
    }

    private static string JoinTrimmedBlock(List<string> lines)
    {
        var start = 0;
        var end = lines.Count - 1;

        while (start <= end && lines[start].Trim().Length == 0)
        {
            start++;
        }

        while (end >= start && lines[end].Trim().Length == 0)
        {
            end--;
        }

        return start > end ? string.Empty : string.Join("\n", lines.GetRange(start, end - start + 1));
    }

    // -------------------------------------------------------- lexical helpers

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

    private static int FirstNonWhitespaceIndex(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int SkipWhitespace(string text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static string FirstToken(string trimmed)
    {
        var position = 0;

        return ReadToken(trimmed, ref position);
    }

    private static string ReadToken(string text, ref int position)
    {
        var start = position;

        while (position < text.Length && !char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return text[start..position];
    }

    private static bool TryReadVnum(
        string text,
        ref int position,
        int lineNumber,
        SGWRoomParseContext context,
        out int vnum
    )
    {
        vnum = 0;
        var token = ReadToken(text, ref position);

        if (token.Length == 0 || !token.All(char.IsAsciiDigit))
        {
            context.Report(SGWDiagnosticCode.ExpectedVnum, lineNumber, $"expected a room vnum, found '{token}'");

            return false;
        }

        if (!long.TryParse(token, out var value) || value < MinVnum || value > MaxVnum)
        {
            context.Report(
                SGWDiagnosticCode.VnumOutOfRange,
                lineNumber,
                $"room vnum {token.TrimStart('0').PadLeft(1, '0')} out of range, expected {MinVnum}..{MaxVnum}"
            );

            return false;
        }

        vnum = (int)value;

        return true;
    }

    private static bool TryReadQuotedString(
        string text,
        ref int position,
        int lineNumber,
        SGWRoomParseContext context,
        out string value
    )
    {
        value = string.Empty;

        if (position >= text.Length || text[position] != '"')
        {
            context.Report(SGWDiagnosticCode.UnterminatedQuotedString, lineNumber, "unterminated quoted string");

            return false;
        }

        var builder = new StringBuilder();
        var i = position + 1;

        while (i < text.Length)
        {
            var current = text[i];

            if (current == '"')
            {
                position = i + 1;
                value = builder.ToString();

                return true;
            }

            if (current != '\\')
            {
                builder.Append(current);
                i++;

                continue;
            }

            if (i + 1 >= text.Length)
            {
                break;
            }

            if (!TryAppendEscape(text[i + 1], lineNumber, builder, context))
            {
                return false;
            }

            i += 2;
        }

        context.Report(SGWDiagnosticCode.UnterminatedQuotedString, lineNumber, "unterminated quoted string");

        return false;
    }

    private static bool TryAppendEscape(
        char escaped,
        int lineNumber,
        StringBuilder builder,
        SGWRoomParseContext context
    )
    {
        switch (escaped)
        {
            case '"':
                builder.Append('"');

                return true;
            case '\\':
                builder.Append('\\');

                return true;
            case 'n':
                builder.Append('\n');

                return true;
            case 't':
                builder.Append('\t');

                return true;
            default:
                context.Report(
                    SGWDiagnosticCode.InvalidEscapeSequence,
                    lineNumber,
                    $"invalid escape sequence '\\{escaped}' in quoted string"
                );

                return false;
        }
    }

    private static bool TrySplitAttribute(string trimmed, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        if (trimmed.Length == 0 || (!char.IsAsciiLetter(trimmed[0]) && trimmed[0] != '_'))
        {
            return false;
        }

        var position = 0;

        while (position < trimmed.Length && (char.IsAsciiLetterOrDigit(trimmed[position]) || trimmed[position] == '_'))
        {
            position++;
        }

        var candidate = trimmed[..position];
        position = SkipWhitespace(trimmed, position);

        if (position >= trimmed.Length || trimmed[position] != ':')
        {
            return false;
        }

        key = candidate;
        value = trimmed[(position + 1)..];

        return true;
    }

    private static bool IsQualifiedName(string name)
    {
        if (name.Length == 0 || !name.Contains('.'))
        {
            return false;
        }

        return name.Split('.').All(IsIdentifier);
    }

    private static bool IsIdentifier(string segment)
    {
        if (segment.Length == 0 || (!char.IsAsciiLetter(segment[0]) && segment[0] != '_'))
        {
            return false;
        }

        return segment.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }
}
