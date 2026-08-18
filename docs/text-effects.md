# Text effects

Kawoosh can send text a piece at a time instead of all at once: a pause before a
line, or a line that types itself out character by character. The effects are
written into the text itself, so a screen, a message, or anything the server
says can use them without a line of code changing.

## 1. Overview

Any text the server sends is compiled into a **script**: a list of steps, each
step being some text and how long to wait before sending it. Text containing no
directives compiles to a single step with no delay, and a single step with no
delay is sent exactly the way text was sent before this existed. Nothing has to
opt in, and nothing that does not use a directive behaves differently.

The compiler is pure — no session, no clock — so what a piece of text does is
decided entirely by reading it.

## 2. Directives

A directive is a whole line. There are two.

| Directive | Argument | Effect |
| --- | --- | --- |
| `@delay <ms>` | non-negative integer | Wait this long before sending what follows |
| `@typewriter <ms>` | non-negative integer | Send following lines one character at a time, this long between characters. `0` turns it off |

Directives are consumed: they never reach the player.

## 3. A worked example

```
The door is locked.
@delay 800
Something moves behind it.
@typewriter 60
Slowly, the handle turns.
@typewriter 0
The door swings open.
```

The player sees `The door is locked.` at once, then nothing for 800 ms, then
`Something moves behind it.` at once. `Slowly, the handle turns.` appears one
character at a time, 60 ms apart. `The door swings open.` arrives whole, because
`@typewriter 0` turned typing off again.

## 4. Rules

**A directive must start at column zero.** A line with any leading whitespace is
text. `@delay 500` is a directive; ` @delay 500` is a line that shows the words.

**An argument that is not a non-negative integer makes the line text.**
`@delay soon` and `@delay -1` are both shown to the player as written. So is any
directive that does not exist, such as `@colour red`.

These two rules are also the escape. ASCII art lives on characters like `@`, and
art that happens to contain the word `@delay` should not silently become a
pause. Indent the line, or leave the argument as it is if it was never a number:
either way it shows.

**Delays add up.** Two `@delay` lines in a row are one longer pause:

```
@delay 200
@delay 300
```

is a 500 ms wait. A delay before any text delays the first thing sent; a delay
at the very end of the text is dropped, because there is nothing left to wait
for.

**A delay before typewritten text applies to the first character only.** With
`@typewriter 30` in force, a preceding `@delay 500` means 530 ms before the
first character and 30 ms before each one after it.

**Typewriter stays on until switched off.** It applies to every line that
follows, not just the next one, until `@typewriter 0` or the end of the text.

**Typewriting is expanded when the text is compiled**, one step per character.
Nothing counts characters at playback time.

## 5. Playback is interruptible

A player who types a line while a script is playing gets the rest of it at once,
and that line is **not** treated as a command. Someone who has read enough and
hits enter should not find they have also walked north.

Text with no directives is never affected by this: it is a single instant step,
sent in one write, and there is no window in which to interrupt it.

## 6. Two things worth knowing

**The tick is the floor.** The game loop runs on a 10 ms tick, so a delay is
rounded up to the next one: `@typewriter 45` measures at roughly 50 ms between
characters. Anything below 10 ms is the same as 10.

**A screen cannot open with a directive.** A `.sgs` file whose first non-blank
line starts with `@` is a metadata block — that rule belongs to the screen
format and is what makes `@clear true` work. A screen beginning with

```
@delay 500
```

reads that line as metadata, not as a pause. Put the directive after the `---`
that closes the metadata block, or after the first line of text. This does not
apply to `.sgm` messages, which have no metadata block.
