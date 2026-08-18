# Code Convention — Kawoosh

Coding conventions for the Kawoosh MUD server. Intentionally strict: every rule below is
enforceable on review against a concrete file in this repository.

Stack: C# / .NET 10 (`net10.0`), `ImplicitUsings` and `Nullable` enabled on every project,
Serilog for logging, NUnit 4 for tests.

## 1. General Principles

- **KISS.** No abstraction without a second concrete use case.
- Prefer an existing local pattern over a new one.
- Keep domain boundaries explicit; keep files small and single-purpose.
- Avoid hidden magic and implicit behavior.
- Write code that is easy to reason about while debugging.

## 2. Repository Layout

```
src/<Project>/          production code
tests/<Project>/        test projects
docs/                   committed specifications (see §12)
```

| Project | Role | May reference |
|---|---|---|
| `Kawoosh.SGW` | World-file format: parsing, data model, diagnostics | Serilog only |
| `Kawoosh.Server` | Executable host | Domain libraries |
| `Kawoosh.Tests` | Test project | Every production project |

`Kawoosh.SGW` is a leaf library. It must stay free of host, transport, and hosting
concerns — it parses text and returns data. Do not add a project reference to
`Kawoosh.Server` from it, in either direction beyond host → library.

## 3. Namespaces

### 3.1 Folder-to-Namespace Rule

The namespace must match the folder path exactly.

```
src/Kawoosh.SGW/Services/SGWFileParser.cs      → namespace Kawoosh.SGW.Services;
src/Kawoosh.SGW/Types/SGWDirection.cs          → namespace Kawoosh.SGW.Types;
tests/Kawoosh.Tests/SGW/Services/…Tests.cs     → namespace Kawoosh.Tests.SGW.Services;
```

### 3.2 Mandatory Buckets

| Bucket | Content | Example in repo |
|---|---|---|
| `Types` | Enums and type constants, domain-prefixed | `Types/SGWDiagnosticCode.cs` |
| `Data` | DTOs, records, simple data carriers | `Data/SGWRoom.cs` |
| `Data.Internal.<Subdomain>` | Internal-only data models | — |
| `Interfaces` | Contracts only | `Interfaces/ISGWFileParser.cs` |
| `Services` | Service implementations | `Services/SGWFileParser.cs` |
| `Internal` | Implementation details outside the public API | `Internal/SGWTokenTables.cs` |
| `Exceptions` | Exception types | `Exceptions/SGWParseException.cs` |
| `Screens` | One `IScreen` implementation per file. A screen is a unit of conversation with one player, not a service, and holds no per-session state. | `Screens/GreetingScreen.cs` |

Group by domain first, never by technical suffix.

## 4. C# File and Type Rules

- One `.cs` file contains at most **one** primary type (`class` OR `record` OR `enum` OR
  `interface`).
- File name matches the type name.
- File-scoped namespaces.
- **No primary constructors.**
- **No expression-bodied constructors** (`public X(...) => ...`); a constructor always has a
  `{ }` body. Expression-bodied *methods* and *properties* are fine.
- **No local functions.** A function declared inside another function is not allowed — extract
  it to a `private static` method and pass the state it needs explicitly. When several helpers
  need the same mutable state, introduce a context type under `Internal` (see
  `Internal/SGWRoomParseContext.cs`) rather than closing over locals.

## 5. Class Layout Order

Inside a type:

1. `const` fields
2. `private readonly` fields (prefixed `_`)
3. Non-readonly fields
4. Properties
5. Constructor(s)
6. Public methods
7. Protected methods
8. Private methods
9. `Dispose` / `DisposeAsync` — **always last**

Properties come **before** constructors, including computed ones.

```csharp
private readonly ILogger _logger = Log.ForContext<SGWFileParser>();
```

## 6. Interfaces

- Interfaces live only under an `Interfaces` namespace, one `I<Name>` per file.
- Every interface **and every member** carries XML docs (`///`).

## 7. Enums

- Enums live under a `Types` namespace.
- The enum name always carries its domain prefix: `SGWDirection`, `SGWDiagnosticCode`,
  `SGWRoomSection`.
- An enum used only inside one assembly is declared `internal`, but still lives in `Types`.

> **Known debt:** `Types/RoomFlag.cs` predates this rule and should become `SGWRoomFlag`.

## 8. Strings

Empty strings: **no rule**. Neither `""` nor `string.Empty` is mandated — leave whichever
form is already there, do not convert between them, and do not raise either one in review.

## 9. Nullability

- `Nullable` is enabled on every project; keep it that way.
- Avoid the null-forgiving operator (`!`) unless the invariant is guaranteed a few lines
  above and obvious to the reader.
- Initialize non-nullable reference properties at declaration rather than leaving them to a
  constructor that may not run.

## 10. Logging

- Serilog, used **statically** via `Log.ForContext<T>()`. Do not inject `ILogger<T>` via DI.
- Declare the logger as a `private readonly` field initialized inline.
- Use static message templates with named placeholders; never string interpolation.

```csharp
_logger.Error("World file not found: {FilePath}", filePath);
```

## 11. Dependency Injection

When a dependency arrives through the constructor from the DI container, do **not** guard it
with `ArgumentNullException.ThrowIfNull`. Let the container validate required services.
Guard clauses remain correct for public API arguments that come from callers.

## 12. File Formats and Specifications

`.sgw` (StarGate World) is specified in `docs/sgw-format.md`. That document is the
**contract**, not a description of the parser.

- The spec is committed and versioned; it is the one artifact in `docs/` that belongs in the
  repository.
- Behaviour changes start in the spec, then in tests, then in the parser.
- Every diagnostic the parser can emit has a code and an exact message in the spec catalogue
  (§6.4). Adding a diagnostic without adding its catalogue row is incomplete work.
- If the implementation cannot satisfy a spec rule, say so **in the spec** (as the "Not yet
  enforced" note does) rather than letting spec and code drift apart silently.

## 13. Parser Diagnostics

Rules for anything that reads authored content and reports problems:

- Render every diagnostic as `<file>:<line>: <message>`; warnings insert a `warning: `
  marker after the location.
- Messages start lowercase and carry no trailing period.
- Collect **all** diagnostics in one pass; never stop at the first error.
- Offer a non-throwing entry point that returns the diagnostics
  (`TryParseRoom(content, fileName)`) alongside a throwing convenience wrapper
  (`ParseRoom(content)`).
- Errors fail the load; warnings alone never do.
- An input that produced an error is excluded from the resulting model.

## 14. Tests

### 14.1 Structure

```
tests/<Project>/<Domain>/<Subdomain>/<Subject>Tests.cs
namespace <Project>.Tests.<Domain>.<Subdomain>;
```

```
tests/Kawoosh.Tests/SGW/Services/SGWFileParserTests.cs
    → namespace Kawoosh.Tests.SGW.Services;
```

- The test folder tree mirrors the production tree.
- **No test file in the test project root.** Every test lives under a domain folder; if the
  domain folder does not exist yet, create it.
- Integration, contract, and performance tests go in dedicated folders or projects
  (`Integration/`, `Contract/`, `Performance/`).
- Shared fixtures, fakes, and builders go in `Support/` — never mixed into domain tests.

### 14.2 Naming

- File and class: `<Subject>Tests`.
- One main test class per file.
- Method style: `Method_Scenario_ExpectedResult`.

### 14.3 Writing Tests

- NUnit 4 constraint model (`Assert.That`). Group related assertions in `Assert.Multiple`.
- Build whitespace-sensitive fixtures with an explicit line-joining helper rather than raw
  string literals, so indentation is visible and exact.
- A new test must be watched failing before the code that makes it pass is written. A test
  that has never failed proves nothing.
- Prefer real objects over mocks.

## 15. Formatting

Driven by `.editorconfig` — do not fight it:

- UTF-8, 4-space indent, spaces not tabs.
- Max line length **125**.
- Trim trailing whitespace; insert a final newline.
- Using directives ordered: `System.*` first, then third-party, then project namespaces.

## 16. Commits

- Conventional Commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, …).
- Scope to the affected subsystem: `feat(sgw):`, `fix(server):`, `test(sgw):`.
- Title **and** body in English.
- **Never** add `Co-Authored-By: Claude` or any AI attribution, in commits, PR bodies,
  issues, or any other text.

## 17. Documentation Discipline

- Plan and design docs go to `~/docs/plans/kawoosh/` and are **never** committed.
- Nothing under `docs/plans/` or `docs/superpowers/` is ever committed.
- `docs/sgw-format.md` is the exception: format specifications are committed (§12).

## 18. Secrets

Every credential — password, API key, token, PSK, passphrase — lives in Bitwarden and is
read through the `bw` client. Never in plaintext in a file, note, config, repo, or commit.
Config and notes carry only the reference `🔑 Bitwarden "<item>"`.

## 19. Non-Negotiable Hygiene

- No dead code.
- No TODO comments without a tracked follow-up.
- No local functions.
- No primary constructors.
- No expression-bodied constructors.
- No inconsistent naming across domains.
- Keep the build warning-free; do not normalize noisy warnings.
- No AI attribution anywhere.

## 20. Additional Conventions

**Async naming**
- Async methods end with `Async`.
- Include a `CancellationToken` on I/O-bound public async methods.

**Exception handling**
- Never swallow an exception silently.
- Log with context before rethrowing; do not replace the stack trace with a bare `throw ex`.

**Collection exposure**
- Prefer `IReadOnlyList<>` / `IReadOnlyDictionary<>` where mutation by callers is not
  intended. Mutable `List<>` properties are acceptable on parser output DTOs that the parser
  itself populates.

**No magic numbers**
- Replace protocol, range, and timing literals with named constants
  (`MinVnum`, `MaxVnum`, `InMemoryFileName`).

**Lookup tables**
- Token/alias tables belong in a static table type under `Internal`, keyed by the lowercased
  token, with the canonical value as the payload — not scattered `switch` statements.
