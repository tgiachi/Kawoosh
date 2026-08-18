# SGS Screen Format

**Format name:** StarGate Screen
**File extension:** `.sgs`
**Status:** normative

A `.sgs` file is one screen the server sends to a player: a login banner, the message of the
day, a race-selection list. Optional metadata, then verbatim text.

## Why the body is verbatim

The world format (`docs/sgw-format.md`) gives meaning to the first character of a line: `#`
is a comment, `>` is an exit, `\` is an escape. That is right for rooms, where the structure
matters and the prose is incidental.

Screens are the opposite. They are mostly ASCII art, and art lives on exactly those
characters — `#`, `|`, `\`, `/`, `_`, `>`. A banner drawn in hashes would be eaten line by
line by a comment rule. So in the body of a `.sgs` file **nothing is special**: no comments,
no escapes, no line-start significance. What the file holds is what the player sees.

## Shape

```
@author Squid
@since 2026-08-18
@description The screen shown before login
---
==============================================
              K A W O O S H
==============================================

Ci sono {onlineCount} giocatori collegati.
```

## Metadata

The metadata block exists **only when the file's first non-blank line starts with `@`**.
Otherwise the whole file is body.

That rule is not cosmetic. `---` is the most common divider in MUD art:

```
------------------------------
       BENVENUTO IN KAWOOSH
------------------------------
```

If `---` always closed a metadata block, this screen would lose its first line. Because the
file does not open with `@`, it is body from the first byte and nothing is consumed.

- Each line is `@key value`. The key matches `[A-Za-z][A-Za-z0-9_-]*`; the value is the rest
  of the line, trimmed. A directive with no value is legal and stores an empty string.
- Keys are matched without regard to case and stored lowercased.
- Blank lines inside the block are ignored.
- The block ends at the first line that is exactly `---` after trimming. Every later `---` is
  body.
- **Unknown keys are kept, not rejected.** Metadata is documentation, not behaviour: rejecting
  `@mood` would mean editing code every time a writer wants a new note. The cost is that a
  typo in a key is silent.

## Body

Everything after the separator, verbatim. A trailing newline at end of file is stripped —
editors add one and the art did not ask for a blank last line. Nothing else is touched;
interior blank lines are kept.

`{name}` tokens are substituted when the screen is rendered, not when it is read, because a
value like a player's name differs per session. `{{` and `}}` escape a literal brace. See
`VariableService`.

## Encoding

UTF-8. A leading BOM is accepted and stripped. `CRLF` and lone `CR` are normalised to `LF`.

## Errors and warnings

Both render as `<file>:<line>: <message>`, the shape the world parser uses, so an operator
reads one format.

**Errors** — the screen directory fails to load:

| Condition |
|---|
| a metadata block never closed by `---` |
| a line inside the metadata block that is neither `@key value` nor blank |
| a directive with no name (`@` alone) |
| an empty body |
| a screen declared required by the caller that is absent from the directory |

An unclosed metadata block does **not** also report an empty body: there was no body section
to read, and one fault should not be dressed as two.

**Warnings** — the load succeeds:

| Condition |
|---|
| a metadata key repeated; the last value wins |

## Naming

The screen's key is its file name, lowercased, without the extension: `Greeting.SGS` is the
screen `greeting`. Nothing has to be kept in sync, and the filesystem already forbids
duplicates.

## The one edge case

Art whose first non-blank line starts with `@` is read as a metadata block and fails on the
missing separator. Prefix that line with a space — the body preserves leading whitespace.

This is documented rather than solved because every additional rule takes another character
away from the art.
