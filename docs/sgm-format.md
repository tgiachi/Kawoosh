# SGM Message Format

**Format name:** StarGate Messages
**File extension:** `.sgm`
**Status:** normative

A `.sgm` file holds the short texts the server says: prompts, refusals, combat lines. One per
line, keyed.

## Why this is not a screen

Screens (`docs/sgs-format.md`) are a handful of long texts, mostly ASCII art, and they get a
file each. Messages are the opposite: hundreds of one-liners. A file each would be hundreds of
one-line files.

The dividing rule is length, not importance. If it needs more than a line or two, it is a
screen.

## Shape

```
# Prompts shown during login.
login.name-prompt = By what name are you known? 
login.name-taken = That name is already in use.
login.welcome = Welcome back, {playerName}.

combat.hit = {attacker} hits {target} for {damage} damage.
```

## Lines

- A line is `key = value`.
- A line whose first non-blank character is `#` is a comment. Blank lines are ignored.
- Whitespace around `=` belongs to neither side. The **trailing** whitespace of a value is
  kept: `Password: ` needs the space after the colon, and trimming it would make every prompt
  in the game subtly wrong.
- The first `=` splits the line, so a value may contain more of them.
- `\n` in a value becomes a line feed.
- A value may be empty. A deliberately blank message is a legitimate thing to want.

## Keys

- A key matches `[A-Za-z][A-Za-z0-9_.-]*`. Dots are ordinary characters, not structure.
- Keys are matched without regard to case.
- **A key is written in full in the file; the file name is organisation only.** `login.sgm`
  holding `login.name-prompt` is redundant, but the alternative — prefixing the key with the
  file name — would make `Render("login.name-prompt")` ungreppable: you would have to know
  which file to look in first.
- The price of that choice is that the same key can appear in two files. That is an error, and
  it names the file and line of the second one. Silently keeping one of the two would make
  behaviour depend on directory order.

## Substitution

`{name}` tokens are replaced when the message is rendered, through the same resolver screens
use. `{{` and `}}` escape a literal brace.

Two sources, in this order:

1. **Per-call arguments** — `Render("combat.hit", ("attacker", name), ("target", other))`.
   A combat line names a different attacker every time and cannot be a registered variable.
2. **Registered variables** — `{serverName}`, `{onlineCount}`.

Arguments win, so a global that happens to be called `target` cannot hijack a message about
one. A substituted value is never re-examined, so a player-supplied name that looks like a
token stays text.

## Errors

All render as `<file>:<line>: <message>`, the shape the other parsers use. Any of them fails
the whole directory load:

| Condition |
|---|
| a line that is neither blank, a comment, nor `key = value` |
| a message with no key |
| a key that is already defined, in this file or another |
| a key declared required by the caller that is absent from the directory |
