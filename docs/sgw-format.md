# SGW File Format Specification

**Format name:** StarGate World
**File extension:** `.sgw`
**Version:** 1.0
**Status:** normative

This document is the single source of truth for the `.sgw` format. A parser must be
implementable from this document alone, without consulting any existing code.

---

## 1. Overview

### 1.1 Purpose

A `.sgw` file describes the **static world data** of a MUD: rooms, their textual
descriptions, their physical connections, and the doors between them. It is a
line-oriented, plain-text, human-editable format designed to be diffable in version
control and writable by hand in any text editor.

### 1.2 Static data vs. behaviour

The format carries **no logic**. There is no expression language, no conditionals, no
loops, and no embedded script bodies.

Behaviour is attached by **reference**: a room may declare `@script` lines that name an
external handler by its fully qualified name. The parser's only job is to validate the
syntactic shape of that name and store it as a string. Resolving the name to an actual
handler, verifying that it exists, and invoking it are the responsibility of a later
**binding** stage, which is out of scope for this specification.

This split means a `.sgw` file can always be parsed and validated offline, with no
runtime, no engine, and no script assemblies present.

### 1.3 Document model

A file contains zero or more **room blocks**. Each room block owns:

| Element | Cardinality | Purpose |
|---|---|---|
| header | exactly 1 | vnum + display name |
| `sector:` | exactly 1 | terrain type |
| `flags:` | 0 or 1 | behavioural markers |
| description | 0 or 1 | free multi-line prose |
| `extra` block | 0..n | keyword-addressable sub-descriptions |
| exit line (`>`) | 0..n | connection to another room |
| `@script` line | 0..n | external behaviour reference |

### 1.4 Lexical conventions

| Aspect | Rule |
|---|---|
| Encoding | UTF-8. A leading BOM (`EF BB BF`) is accepted and stripped. Any other encoding is an error. |
| Line terminators | `LF`, `CRLF`, and lone `CR` are all accepted and normalised to `LF`. |
| Final newline | Optional. A last line with no terminator is a valid line. |
| Line numbering | 1-based, counted **after** normalisation, over the raw file including comments and blank lines. |
| Structure | Strictly line-oriented. No construct spans a line boundary except free text, which is explicitly multi-line. |
| Horizontal whitespace | `SPACE` (U+0020) and `TAB` (U+0009). Referred to below as `WS`. |
| Keyword case | All directives, keywords and enumerated tokens are **case-insensitive** (`@ROOM`, `@Room`, `@room` are identical). |
| Value case | Text inside double quotes, free text, and script names are **case-sensitive** and preserved verbatim. |
| Reserved prefix | A line whose first non-whitespace character is `@` at structural level is a directive. Unknown directives are an error, not free text. |

### 1.5 Blank lines

A **blank line** is a line containing only `WS` (possibly empty).

- Outside a room block: ignored.
- Inside a room block, before the description has started: ignored.
- Inside a description or an `extra` block: significant — see §2.5 and §2.6.

### 1.6 Comments

A **comment line** is a line whose first non-whitespace character is `#`. The entire
line is discarded before any other parsing.

Comments are recognised **everywhere**, including inside descriptions and `extra`
blocks. There are no trailing/inline comments: a `#` that is not the first
non-whitespace character of a line is an ordinary literal character.

```sgw
# this whole line is a comment
   # indented comments are comments too
sector: forest        # <-- NOT a comment, this is part of the value and will fail
```

### 1.7 Line-start escape

To write a line of free text that would otherwise be interpreted as structure, prefix
it with a backslash. If the **first non-whitespace character of a line is `\`**, that
single backslash is removed and the remainder of the line is taken as literal free
text, never as structure.

This one rule covers every collision: leading `#`, leading `>`, a line starting with
`@end`, a line starting with `extra "`, a first description line shaped like
`word: value`.

| Written | Yields |
|---|---|
| `\# not a comment` | `# not a comment` |
| `\> not an exit` | `> not an exit` |
| `\@end is just text` | `@end is just text` |
| `\Warning: mind the gap` | `Warning: mind the gap` |
| `\\literal backslash first` | `\literal backslash first` |

The escape is only meaningful at line start. A backslash anywhere else in free text is
an ordinary character. The escape is **not** available inside quoted strings, which
have their own escape rules (§2.2).

---

## 2. Formal grammar

Notation: EBNF. `{ x }` = zero or more, `[ x ]` = optional, `|` = alternative,
`"..."` = literal (case-insensitive for keywords), `?...?` = prose-defined terminal.
`NL` is a line terminator or end-of-file. `WS` is one or more horizontal whitespace
characters. `[WS]` is optional horizontal whitespace.

### 2.1 File

```ebnf
file            = { file-item } ;

file-item       = comment-line
                | blank-line
                | room-block ;

comment-line    = [WS] , "#" , { ?any character? } , NL ;
blank-line      = [WS] , NL ;
```

Any other content at file level — text, a stray `>` line, a stray `@end` — is an error
(`E010`, `E011`).

### 2.2 Terminals

```ebnf
vnum            = digit , { digit } ;                  (* decimal, no sign, no separators *)
digit           = "0".."9" ;

quoted-string   = '"' , { qs-char } , '"' ;
qs-char         = ?any character except '"', '\', CR, LF?
                | "\" , ( '"' | "\" | "n" | "t" ) ;

identifier      = ( letter | "_" ) , { letter | digit | "_" } ;
letter          = "A".."Z" | "a".."z" ;

qualified-name  = identifier , "." , identifier , { "." , identifier } ;
```

**`vnum` constraints:** decimal only. Valid range is `0 .. 2147483647`, the non-negative
half of a signed 32-bit integer. `0` is an ordinary vnum; the format reserves no sentinel
value. Leading zeros are accepted and insignificant (`00042` == `42`). A token that is not
a run of decimal digits — including a missing token, a sign, or `0x` notation — is `E023`;
a well-formed decimal number above the range is `E020`.

**`quoted-string` escapes:** exactly four are recognised — `\"` → `"`, `\\` → `\`,
`\n` → LF, `\t` → TAB. Any other character after a backslash is `E021`. A quoted string
may not span lines; an unterminated quote is `E022`.

**`qualified-name`:** at least one dot is required — a bare identifier is not a fully
qualified name (`E060`). No whitespace is permitted inside.

### 2.3 Room block

```ebnf
room-block      = room-header ,
                  { attribute-line | comment-line | blank-line } ,
                  [ description ] ,
                  { room-element } ,
                  end-line ;

room-header     = [WS] , "@room" , WS , vnum , WS , quoted-string , [WS] , NL ;

end-line        = [WS] , "@end" , [WS] , NL ;

room-element    = extra-block
                | exit-line
                | script-line
                | comment-line
                | blank-line ;
```

Rules:

- The three sections are **ordered**: attributes, then description, then elements.
  Within the element section, `extra` / `>` / `@script` may appear in any order and
  interleave freely.
- Room blocks do **not** nest. Encountering `@room` while a block is open is `E012`.
- Any content after the closing `"` of the header, other than `WS`, is `E013`.
- Reaching end-of-file with a block still open is `E014`.

### 2.4 Attribute lines

```ebnf
attribute-line  = sector-line | flags-line ;

sector-line     = [WS] , "sector" , [WS] , ":" , [WS] , sector-token , [WS] , NL ;

flags-line      = [WS] , "flags"  , [WS] , ":" , [WS] , flag-list  , [WS] , NL ;

flag-list       = flag-token , { flag-sep , flag-token } ;
flag-sep        = [WS] , [ "," ] , [WS] ;     (* at least one WS or one comma required *)
```

- `sector` is **required**, exactly once per room (`E030` if missing, `E031` if
  repeated).
- `flags` is **optional**, at most once per room (`E031` if repeated). When absent, the
  room has an empty flag set.
- Attribute keys are case-insensitive. Whitespace is allowed on both sides of the colon.
- The attribute section ends at the first line that is not blank, not a comment, and not
  a well-formed attribute line.
- A line in the attribute section matching the shape `identifier [WS] ":"` whose key is
  neither `sector` nor `flags` is `E032` (unknown attribute) — it is **not** silently
  treated as the start of the description. To begin a description with such a line, use
  the line-start escape (§1.7).
- An empty value (`sector:` with nothing after it) is `E033`.

### 2.5 Description

```ebnf
description     = description-line , { description-line } ;

description-line = ?any line that is not: a comment-line, a well-formed attribute-line
                   in attribute position, an exit-line, a script-line, an extra-block
                   opener, or an end-line? , NL ;
```

The description begins at the first line of the room body that is not blank, not a
comment, and not an attribute line. It ends immediately before the first subsequent
line that:

1. begins an `extra` block, **or**
2. is an exit line (`>`), **or**
3. is a `@script` line, **or**
4. is the `@end` line.

Nothing else terminates it. Blank lines, lines containing colons, lines starting with
`@` that are not `@script`/`@end` (which are `E015`, unknown directive) — all covered by
the four terminators above and the escape rule.

**Text assembly:**

1. Comment lines inside the description are removed entirely (they do not leave a blank
   line behind).
2. The line-start escape is applied and the backslash removed.
3. Trailing horizontal whitespace is stripped from every line.
4. Leading horizontal whitespace is **preserved**, so indentation and ASCII art survive.
5. Lines are joined with a single `LF`.
6. Leading and trailing blank lines of the whole block are removed. Interior blank lines
   are preserved as paragraph separators.

A room with no description is legal but produces warning `W001`; the description value
is the empty string.

### 2.6 Extra block

```ebnf
extra-block     = extra-opener , { extra-continuation } ;

extra-opener    = [WS] , "extra" , WS , quoted-string , [WS] , ":" , [ inline-text ] , NL ;

inline-text     = ?any characters up to end of line? ;

extra-continuation = ( WS , ?any characters up to end of line? ) , NL
                   | blank-line ;
```

- The quoted string holds one or more **keywords** separated by whitespace. Every
  keyword is an independent alias for the same extra description. Keywords are
  case-insensitive on lookup and stored lowercased. An empty keyword string is `E040`.
- The block body is the `inline-text` after the colon (if any), followed by every
  continuation line.
- A **continuation line** is any line that starts with at least one `WS` character, or is
  blank. The block ends at the first non-blank line whose first character is not `WS`.
- Because a blank line does not close the block, an `extra` followed by a blank line and
  then an unindented `>` exit line is unambiguous: the exit closes the extra.
- **Trailing blank lines are not part of the text** — they are trimmed, so the exit line
  case above yields no spurious trailing newlines.

**Text assembly:** identical to §2.5 steps 1–6, except that leading whitespace on
continuation lines is stripped in one pass by removing the **longest common leading
whitespace prefix** of all non-blank continuation lines. This preserves relative
indentation while normalising the block's base indent. Inline text after the colon is
left-trimmed and becomes the first line.

- Duplicate keyword within the same room (across all its extra blocks) is `E041`.
- An extra block with no text at all (empty inline text and no continuations) is `E042`.
- A quoted keyword list not followed by `:` is `E043`.
- A line starting with `extra` whose next non-whitespace character is **not** a `"` is not an
  extra opener at all — it is ordinary free text, so a description may begin with
  "extra care is needed" without an escape.

### 2.7 Exit line

```ebnf
exit-line       = [WS] , ">" , [WS] , direction , WS , vnum ,
                  [ WS , door-spec ] , [WS] , NL ;

door-spec       = "door" , WS , quoted-string , { WS , door-modifier } ;

door-modifier   = "locked"
                | "key" , "=" , vnum ;
```

- `direction` is any token from §3, canonical name or alias, case-insensitive. Unknown
  direction is `E050`.
- The target `vnum` is not resolved at parse time; §5 covers the cross-reference check.
- `door-spec` is optional. When present, its quoted string is the door's display name and
  may be empty (`""`), meaning "an unnamed door".
- `door-modifier`s may appear in **any order**, each at most once (`E053` on repeat).
  Canonical written order is `locked` then `key=<vnum>`.
- **No whitespace is permitted around the `=`** in `key=<vnum>`. `key = 1234` is `E054`.
- `locked` or `key=` without a preceding `door` keyword is `E051`.
- Any other unrecognised token after the target vnum is `E055`.
- Two exits in the same room resolving to the same canonical direction is `E052`, even if
  written with different aliases (`> n 100` and `> north 200`).
- A `key` vnum refers to an object vnum, in a namespace independent from room vnums. The
  parser performs no cross-check on it.

Examples:

```sgw
> north 3002
> n 3002
> east 3010 door "an oak door"
> west 3011 door "an iron gate" locked
> down 3012 door "a trapdoor" locked key=9001
> up   3013 door "a hatch" key=9002 locked
> south 3014 door ""
```

### 2.8 Script line

```ebnf
script-line     = [WS] , "@script" , WS , event , WS , qualified-name , [WS] , NL ;

event           = "enter" | "exit" | "look" ;
```

- `event` is case-insensitive; unknown event is `E061`.
- Each event may be declared at most once per room (`E062`). There is no "last wins"
  behaviour.
- `qualified-name` is stored verbatim, case-sensitive. The parser does **not** attempt to
  load, resolve, or validate the existence of the target (§1.2).
- Trailing content after the name is `E063`.

| Event | Fired when |
|---|---|
| `enter` | a character finishes moving into the room |
| `exit` | a character is about to leave the room |
| `look` | a character looks at the room |

```sgw
@script enter Kawoosh.Scripts.Temple.OnEnter
@script look  Kawoosh.Scripts.Temple.OnLook
```

---

## 3. Directions

Six directions are valid. Aliases are equivalent in every way; the parser stores the
canonical form.

| Canonical | Aliases | Opposite |
|---|---|---|
| `north` | `n` | `south` |
| `south` | `s` | `north` |
| `east` | `e` | `west` |
| `west` | `w` | `east` |
| `up` | `u` | `down` |
| `down` | `d` | `up` |

The **Opposite** column is informational: it defines the reciprocal used by the
optional one-way-exit warning (§5, `W002`). It imposes no requirement that exits be
symmetric.

Diagonals (`northeast`, `ne`, `northwest`, `nw`, `southeast`, `se`, `southwest`, `sw`)
are **reserved for a future version** and must currently be rejected with `E050`,
not silently accepted.

---

## 4. Enumerations

### 4.1 Room flags

Value of the `flags:` attribute. Case-insensitive, whitespace- and/or comma-separated.

| Canonical token | Aliases | Meaning |
|---|---|---|
| `none` | — | Explicitly empty set. Must be the only token in the list. |
| `dark` | — | No light source present; requires light to see. |
| `no_mob` | `nomob` | NPCs may not enter or be created here. |
| `indoor` | `indoors` | Sheltered; weather and outdoor effects do not apply. |
| `safe` | — | No combat may be initiated or resolved here. |
| `private` | — | Hard cap of two occupants. |
| `solitary` | — | Hard cap of one occupant. |
| `no_recall` | `norecall` | Recall/teleport-out effects fail here. |

Rules:

- An unknown token is `E070`.
- A repeated flag in the same list is `E071`.
- `none` combined with any other token is `E072`.
- `flags:` present with an empty value is `E033`.
- The flag `indoor` and the sector `indoor` are unrelated tokens in separate namespaces;
  declaring one does not imply the other.

```sgw
flags: dark no_mob no_recall
flags: safe, indoor
flags: none
```

### 4.2 Sectors

Value of the `sector:` attribute. Exactly one token, case-insensitive.

| Canonical token | Aliases | Meaning |
|---|---|---|
| `indoor` | `inside` | Inside a building or structure. |
| `city` | `urban` | Paved street or plaza. |
| `field` | `plain` | Open grassland. |
| `forest` | `wood` | Dense trees. |
| `hills` | `hill` | Rolling elevation. |
| `mountain` | `mountains` | Steep terrain, slow movement. |
| `desert` | — | Arid open terrain. |
| `road` | `path` | Maintained travel route, fast movement. |
| `swamp` | `marsh` | Boggy terrain, very slow movement. |
| `cave` | `cavern` | Natural underground passage. |
| `dungeon` | — | Constructed underground complex. |
| `water_swim` | `water` | Shallow water; passable by swimming. |
| `water_noswim` | `deepwater` | Deep water; requires a boat. |
| `underwater` | `submerged` | Fully submerged; requires breathing. |
| `air` | `sky` | Open air; requires flight. |

An unknown token is `E034`. More than one token on the line is `E035`.

---

## 5. Validation rules

A conforming parser applies the checks below. Rules marked **(syntax)** are enforced
while reading a single line or block; rules marked **(semantic)** require the whole file
(or the whole world set) and therefore run in a second pass, after all files are read.

This section lists the rules only; algorithms are deliberately unspecified.

### 5.1 Structural

1. **(syntax)** Every `@room` has a matching `@end`; the file does not end with an open
   block.
2. **(syntax)** `@end` never appears without an enclosing `@room`.
3. **(syntax)** `@room` never appears inside an open room block.
4. **(syntax)** Every quoted string opened on a line is closed on the same line.
5. **(syntax)** Every `@` directive is one of `@room`, `@end`, `@script`.
6. **(syntax)** No content other than whitespace follows a construct's last required
   token.

### 5.2 Room-level

7. **(syntax)** The room header carries both a vnum and a quoted name.
8. **(syntax)** The room name is non-empty after unescaping, and at most 128 characters.
9. **(syntax)** `sector:` is present exactly once and holds exactly one known token.
10. **(syntax)** `flags:` appears at most once; every token is known; no token repeats;
    `none` is not mixed with other flags.
11. **(syntax)** No attribute key other than `sector` and `flags` appears in the
    attribute section.
12. **(syntax)** Each `@script` event is declared at most once per room.
13. **(syntax)** Extra keyword strings are non-empty; each keyword is at most 64
    characters; no keyword is declared twice within the same room.
14. **(syntax)** Every extra block carries non-empty text.

> **Not yet enforced.** The length limits in rules 8 and 13 (room name ≤ 128 characters,
> keyword ≤ 64 characters) and the non-empty room name check have no code assigned in the
> catalogue of §6.4, and the reference parser does not apply them. Assign codes before
> relying on them.

### 5.3 Exits

15. **(syntax)** The direction token is one of the six valid directions or their aliases.
16. **(syntax)** No two exits in the same room share a canonical direction.
17. **(syntax)** `locked` and `key=` appear only inside a `door` specification, at most
    once each.
18. **(semantic)** Every exit target vnum refers to a room that exists in the loaded
    world set.
19. **(semantic)** No exit targets its own room's vnum (self-loop), unless intentional —
    reported as a warning, not an error.

### 5.4 World-level

20. **(semantic)** Room vnums are unique across the entire loaded world set, not merely
    within one file. A collision reports both the offending location and the location of
    the first definition.
21. **(syntax)** Every vnum lies in `0 .. 2147483647`.
22. **(semantic)** Optional connectivity report: rooms with no inbound exit from any
    other room are unreachable and reported as a warning.
23. **(semantic)** Optional reciprocity report: an exit `A --dir--> B` with no matching
    `B --opposite(dir)--> A` is one-way and reported as a warning.

### 5.5 Error recovery

24. Parsing does not stop at the first error. The parser collects every diagnostic and
    reports them all, ordered by file then by line.
25. On a **line-level** error, the offending line is discarded and parsing resumes with
    the next line, inside the same room block.
26. On a **block-level** error (bad header, missing `@end`), the parser discards the
    partial room and resynchronises by skipping forward to the next line that is either
    `@end` or a new `@room` header.
27. A room that produced any error is excluded from the resulting world, so that
    semantic checks in the second pass do not cascade off invalid data.
28. The load as a whole fails if at least one error was reported. Warnings alone never
    fail a load.

---

## 6. Diagnostic message format

### 6.1 Shape

Every diagnostic is emitted on a single line:

```
<file>:<line>: <message>
```

- `<file>` — the path as it was given to the parser. Not normalised, not made absolute.
- `<line>` — 1-based line number, as defined in §1.4.
- Exactly one `: ` (colon + single space) separates the line number from the message.
- `<message>` starts with a lowercase letter and carries no trailing period.
- Diagnostics are written to standard error, one per line, sorted by file then line.

```
world/temple.sgw:42: unknown room flag 'no_recal'
world/temple.sgw:57: exit points to unknown room vnum 3099
```

### 6.2 Warnings

Warnings use the same shape with a `warning: ` marker after the location:

```
<file>:<line>: warning: <message>
```

```
world/temple.sgw:12: warning: room 3001 has no description
```

### 6.3 Secondary locations

When a diagnostic references a second location (a duplicate definition, an unterminated
block), a follow-up line is emitted using the same shape, indented by two spaces:

```
world/temple.sgw:80: duplicate room vnum 3001
  world/temple.sgw:12: note: first defined here
```

### 6.4 Message catalogue

Codes are for cross-reference within this document; they are **not** printed in the
message. `{...}` marks interpolated values.

| Code | Message text |
|---|---|
| `E010` | `unexpected text outside of a room block` |
| `E011` | `'@end' without a matching '@room'` |
| `E012` | `'@room' inside an unterminated room block` |
| `E013` | `unexpected text after room name` |
| `E014` | `unterminated room block, expected '@end' before end of file` |
| `E015` | `unknown directive '{directive}'` |
| `E020` | `room vnum {value} out of range, expected 0..2147483647` |
| `E021` | `invalid escape sequence '\{char}' in quoted string` |
| `E022` | `unterminated quoted string` |
| `E023` | `expected a room vnum, found '{token}'` |
| `E030` | `room {vnum} is missing the required 'sector' attribute` |
| `E031` | `duplicate '{key}' attribute` |
| `E032` | `unknown attribute '{key}'` |
| `E033` | `attribute '{key}' has an empty value` |
| `E034` | `unknown sector '{token}'` |
| `E035` | `sector expects a single value, found {count}` |
| `E040` | `extra description has an empty keyword list` |
| `E041` | `duplicate extra keyword '{keyword}' in room {vnum}` |
| `E042` | `extra description '{keyword}' has no text` |
| `E043` | `malformed extra description header, expected ':' after the keyword list` |
| `E050` | `unknown direction '{token}'` |
| `E051` | `'{modifier}' requires a 'door' specification` |
| `E052` | `duplicate exit '{direction}' in room {vnum}` |
| `E053` | `duplicate door modifier '{modifier}'` |
| `E054` | `'key' expects the form key=<vnum> with no spaces` |
| `E055` | `unexpected text after exit definition` |
| `E060` | `'{name}' is not a fully qualified script name` |
| `E061` | `unknown script event '{event}'` |
| `E062` | `duplicate script event '{event}' in room {vnum}` |
| `E063` | `unexpected text after script name` |
| `E070` | `unknown room flag '{token}'` |
| `E071` | `duplicate room flag '{token}'` |
| `E072` | `flag 'none' cannot be combined with other flags` |
| `E080` | `duplicate room vnum {vnum}` |
| `E081` | `exit '{direction}' points to unknown room vnum {vnum}` |
| `W001` | `room {vnum} has no description` |
| `W002` | `exit '{direction}' to room {vnum} is one-way` |
| `W003` | `room {vnum} is unreachable, no room links to it` |
| `W004` | `exit '{direction}' in room {vnum} points to itself` |

---

## 7. Examples

### 7.1 Minimal room

```sgw
# temple.sgw — the smallest valid world file

@room 3001 "The Temple Altar"
sector: indoor

A plain stone altar stands at the centre of the chamber. Candle wax has
pooled on the flagstones over the years, and the air smells faintly of
cold incense.
@end
```

Parsed result:

- vnum `3001`, name `The Temple Altar`
- sector `indoor`, no flags
- description: 3 lines joined with `LF`
- no extras, no exits, no scripts

### 7.2 Room with flags, extras and a locked door

```sgw
# temple-vault.sgw

@room 3002 "The Vault Antechamber"
sector: indoor
flags: dark, no_mob, no_recall

The antechamber is windowless. A single iron door blocks the way east,
its surface engraved with a spiral of unfamiliar glyphs.

\# the numeral above the lintel is part of the carving, not a comment
A carved lintel spans the doorway.

extra "door iron": The door is a single slab of black iron, taller than
    a man and twice as wide. It has no handle on this side — only a
    keyhole worn smooth by centuries of use.

extra "glyphs spiral engraving":
    The glyphs spiral inward from the door's edge. You cannot read
    them, but the pattern repeats every seventh mark.

extra "lintel": Weathered stone, carved with the numeral VII.

> west 3001
> east 3003 door "an iron door" locked key=9001

@script enter Kawoosh.Scripts.Temple.Vault.OnEnter
@script look  Kawoosh.Scripts.Temple.Vault.OnLook
@end
```

Points of interest:

- The `\#` line is free text; the backslash is stripped, leaving `# the numeral ...`.
- The blank line between the two description paragraphs is preserved.
- `extra "door iron"` registers **two** keywords for the same text.
- The second extra has empty inline text and starts on its continuation lines — legal,
  because the block as a whole has text.
- The blank line after each extra does not close it; the next unindented line does.
- Exit `east` is a locked door opened by object vnum `9001`.

### 7.3 Multi-room connected file

```sgw
# ============================================================
#  midgaard-gate.sgw — the north gate approach
#  vnum range: 3100-3104
# ============================================================

@room 3100 "Before the North Gate"
sector: road
flags: safe

The paved road ends at the city's north gate, two leaves of banded oak
set into a stone arch. Traffic thins here as the light fails; the guards
have already begun to eye the horizon.

extra "gate gates oak": Each leaf is a hand's breadth thick, banded in
    iron and pitted with old arrow scars.

> north 3101 door "the north gate"
> south 3103
@end


@room 3101 "The Gatehouse Passage"
sector: indoor
flags: indoor, no_mob
The passage runs beneath the gatehouse. Murder holes pock the ceiling,
and the flagstones underfoot are grooved by generations of cart wheels.

> south 3100 door "the north gate"
> north 3102

@script enter Kawoosh.Scripts.Midgaard.Gatehouse.OnEnter
@end


@room 3102 "Northern Market Square"
sector: city

# more exits will be added when the market district is written

Stalls crowd the square shoulder to shoulder. Even at this hour a few
traders linger, folding oilcloth over their goods and arguing prices
with anyone still willing to listen. A capped cistern sits at the centre,
its lid pushed aside.

extra "stalls stall market": Canvas and bare poles, most already packed
    down for the night.

extra "cistern lid": A shaft of dressed stone. The lid lies beside it,
    and the shaft drops further than the light reaches.

> south 3101
> down 3104
@end


@room 3103 "The Road South"
sector: road
flags: dark

The road runs south into open country. Behind you the gate lamps are
being lit one by one; ahead there is nothing but the dark shape of the
treeline.

> north 3100
@end


@room 3104 "The Bottom of the Cistern"
sector: cave
flags: dark, no_recall, no_mob

Black water reaches your knees. The shaft mouth is a grey coin of light
far overhead, well out of reach.

@script enter Kawoosh.Scripts.Midgaard.Cistern.OnEnter
@end
```

The file is valid and loads five rooms. It produces exactly one diagnostic:

```
world/midgaard-gate.sgw:52: warning: exit 'down' to room 3104 is one-way
```

Line 52 is `> down 3104`. Room `3104` declares no exits at all, so the reciprocal `up`
is missing and `W002` fires — deliberate here, since the cistern is a trap. Every other
pair is symmetric: `3100 north ↔ 3101 south`, `3100 south ↔ 3103 north`,
`3101 north ↔ 3102 south`. No room lacks an inbound exit, so `W003` never fires.

Note the two comment lines at 3102's line 38 and the blank lines around it: they sit in
the attribute section, are discarded, and do not start the description. The description
begins at `Stalls crowd the square...`.

---

## 8. Edge cases

Each case below has exactly one correct behaviour. A parser must implement all of them.

### 8.1 Blank line inside a description

Blank lines inside a description are **content**, preserved as paragraph separators.
They never terminate the description — only the four terminators of §2.5 do.

Leading and trailing blank lines of the description block are trimmed, so a blank line
between the last attribute and the first prose line, or between the last prose line and
the first exit, produces no leading/trailing newline in the stored value.

### 8.2 `#` inside free text

A line whose first non-whitespace character is `#` is a comment **even inside a
description or an extra block**, and is removed without leaving a blank line. To emit a
literal leading `#`, escape it: `\#`. A `#` in any other column is a literal character
and needs no escaping.

```sgw
Room #7 is down the hall.          <- literal '#', no escape needed
# this line vanishes
\# this line renders as "# this line renders as ..."
```

### 8.3 A description line that looks like an attribute

Only the **attribute section** — before the description starts — recognises
`identifier:` lines. Once the description has begun, `sector: forest` is prose.

The single risky line is therefore the *first* description line. If it must start with
`word:`, escape it (`\Warning: ...`) or precede it with a line that cannot be an
attribute.

An unrecognised `identifier:` line while still in the attribute section is `E032`, never
a silent fallthrough into the description.

### 8.4 Truncated file, no closing `@end`

The parser reports `E014` at the line of the **`@room` header that was left open**, with
a note pointing at the last line of the file:

```
world/temple.sgw:12: unterminated room block, expected '@end' before end of file
  world/temple.sgw:88: note: end of file reached here
```

The partial room is discarded and does not enter the world. This is an error, never a
lenient "close it implicitly at EOF".

### 8.5 Malformed lines

| Situation | Behaviour |
|---|---|
| Unrecognised line at file level | `E010`, line skipped, parsing continues. |
| Line inside the element section that is none of `extra` / `>` / `@script` / `@end` / comment / blank | `E010`, line skipped. Prose is not allowed after the description ends. |
| `>` with no direction | `E050`; exit dropped, room kept. |
| `>` with a direction but no vnum | `E023`; exit dropped, room kept. |
| Non-numeric vnum | `E023`; the whole construct (header or exit) is dropped. |
| `@script` with fewer than two arguments | `E060`; line dropped. |
| Unterminated quoted string | `E022`; line dropped, parsing resumes on the next line — the parser never scans forward looking for a closing quote. |
| Two errors on the same line | Only the **first** is reported; the line is dropped. |

### 8.6 Empty room body

`@room` immediately followed by `@end` yields `E030` (missing `sector`) and `W001`
(no description). The room is discarded because it produced an error.

### 8.7 `@end` appearing inside free text

`@end` at the start of a line always closes the room, including in the middle of a
description or an extra block. There is no "inside a string" context that suppresses it.
Use `\@end` to write it as text.

The same applies to a line starting with `>` or `extra "` inside a description: it is
structure, and it ends the description. This is intentional — the description terminator
list of §2.5 is exactly the set of things that cannot be free text without an escape.

### 8.8 Duplicate vnum across files

Room vnums are unique **world-wide**, not per file. Loading two files that both define
vnum `3001` is `E080`, reported at the second definition, with a note pointing at the
first — including its file path, which may differ:

```
world/temple.sgw:12: duplicate room vnum 3001
  world/midgaard.sgw:204: note: first defined here
```

File load order determines which one is "first"; parsers must load files in a
deterministic order (lexicographic by full path) so the diagnostic is reproducible.

### 8.9 Whitespace-only description

A description consisting solely of blank and whitespace-only lines trims to the empty
string and is treated exactly as an absent description: `W001`, no error.

### 8.10 CRLF, tabs and trailing whitespace

`CR` is stripped during normalisation and never reaches the text values. Trailing
whitespace is stripped from every free-text line, so a file saved by an editor that pads
lines produces byte-identical text values to one that does not. Tabs are legal
indentation for extra continuations and count as one whitespace character for the
longest-common-prefix computation of §2.6 — mixing tabs and spaces in one block's indent
therefore yields a shorter common prefix and residual indentation, which is not an
error.

### 8.11 Escaped quotes in names

`@room 3001 "The \"Old\" Inn"` yields the name `The "Old" Inn`. The unescaping happens
after the string is delimited, so the escaped quote never terminates the string. Length
limits (§5.2) are checked on the **unescaped** value.

### 8.12 Very large vnum

A vnum with more digits than fit in a signed 32-bit integer is `E020`, not an overflow or
a wrap-around. The parser must range-check before converting.
