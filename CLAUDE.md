# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Response style

Be terse. No pleasantries, preambles, or question recaps. No "I'd be happy to" or "Great question". Drop articles and filler words where meaning stays clear. Short declarative sentences. Prefer `Use async with try/catch` over `The solution is to use async functions with proper error handling`.

## Skills
Use `csharp-coding-standards` and `csharp-type-design-performance` when writing or refactoring C#.
Use `csharp-concurrency-patterns` for Worker concurrency decisions (hosted services, channels, background jobs).
Use `efcore-patterns` when writing queries, migrations, or repository logic in Infrastructure.
Use `microsoft-extensions-dependency-injection` when registering or restructuring services.
Use `microsoft-extensions-configuration` when working with `appsettings`, user secrets, or environment config.
Use `ilspy-decompile` to inspect third-party NuGet internals when source is unavailable.
Run `simplify` after implementing a feature to catch unnecessary complexity.
Run `security-review` when touching auth, device provisioning, Azure Blob, or Entra endpoints.
Use `code-audit-debugger` when investigating a specific audit finding — paste the finding into the Goal section and run all six phases.
Use the `official-docs-researcher` subagent for any framework, SDK, NuGet package, or external API before implementing.

## Before every coding task

**This is mandatory. Do not write or edit any file until all four steps are complete.**

1. **Codebase research** — run the `codebase-researcher` subagent for the relevant feature area. Read the handler, validator, entity, repository, controller, mappings, and tests. Return a concise terrain summary. This is not skippable based on familiarity with the codebase — memory of a pattern is not the same as reading it.
2. **Docs research** — if the task touches any framework, SDK, NuGet package, or external API that has official documentation, run the `official-docs-researcher` subagent for that specific topic. This is not conditional on confidence level. Training knowledge of a framework does not substitute for a current docs check.
3. **Clarification** — if anything is ambiguous, or if codebase conventions conflict with official best practices, do not silently pick one. Surface the tension, explain the tradeoff, and discuss with the user before proceeding.
4. **Implementation plan report** — present a structured report of what will be created or modified, in what order, and with what approach. Wait for explicit user approval before writing any code.

Use `/pre-task <description>` to run all four phases in sequence.

## Commands

```bash
dotnet build

dotnet run --project src/NotesApp.Api      # HTTPS localhost:7011 · Scalar UI at /scalar
dotnet run --project src/NotesApp.Worker

dotnet test                                # all tests
dotnet test tests/NotesApp.Application.Tests
dotnet test tests/NotesApp.Api.IntegrationTests
dotnet test tests/NotesApp.Worker.Tests

# EF migrations — user runs manually, never automatically
dotnet ef database update --project NotesApp.Infrastructure --startup-project NotesApp.Api
```

## Architecture

Clean Architecture: **Domain → Application → Infrastructure | Api | Worker**.

| Layer | Role |
|---|---|
| Domain | Entities, value objects, domain methods, `DomainResult`/`DomainResult<T>`. No framework references. |
| Application | MediatR commands/queries, FluentValidation validators, DTOs, `*Mappings` files. All handlers return `Result<T>`. |
| Infrastructure | EF Core + SQL Server, repositories, `ICurrentUserService`, `ISystemClock`, Azure Blob, Entra auth. |
| Api | Thin controllers: HTTP → MediatR → `ToActionResult()` / `CreatedAtAction()` / `NoContent()`. |
| Worker | Hosted service: outbox processing and reminder dispatch. |

## Handler conventions

- **Every handler requires a paired validator** in the same folder. Add it before considering the task done.
- Handlers return `Result<T>`; domain methods return `DomainResult`/`DomainResult<T>`. Use existing conversion helpers.
- Route IDs overwrite body IDs.
- Mapping logic belongs in the existing static `*Mappings` files, not in controllers or handlers.
- DTO names follow: `Detail`, `Summary`, `Overview`, `Sync*`.

### Command handler order (update/delete)

1. Resolve user (`ICurrentUserService`) and time (`ISystemClock`).
2. Load entity **untracked**.
3. Ownership / existence checks — return early on failure.
4. Apply domain methods in memory.
5. Create outbox message.
6. `Repository.Update(...)` or `AddAsync(...)`.
7. `UnitOfWork.SaveChangesAsync(...)` once.

Do not attach tracked entities before early-return checks pass.

## Persistence

- Retrieve untracked by default; attach explicitly only after validation passes.
- Prefer repository methods already present in the feature.
- Use `ISystemClock` and `ICurrentUserService` — never `DateTime.UtcNow` or raw `HttpContext` in handlers.
- Preserve soft-delete, versioning, and row-version concurrency patterns.

## Outbox

- Use `OutboxMessage.Create<TAggregate, TEvent>()` with event-type enums — never magic strings.
- Use `OutboxPayloadBuilder` where one exists.
- Save entity change + outbox message in the same `UnitOfWork.SaveChangesAsync` call.

## Sync

- Preserve per-item conflict results, version checks, delete-wins, and device ownership checks inside sync handlers.
- Do not dispatch nested commands inside sync handlers.
- Subtask ordering uses fractional-index `Position` strings — do not replace with integers.

## Domain facts

- Notes are block-based; `Note` has no content field — do not add one.
- `MeetingLink` on tasks is a nullable trimmed string, not a `Uri`.

## Tests

Three test projects: `NotesApp.Application.Tests` (unit, Moq), `NotesApp.Api.IntegrationTests` (full HTTP), `NotesApp.Worker.Tests` (unit, Moq).

- Match the testing level already used in the area you touch.
- Naming: `[Feature][Command|Query]HandlerTests`, `[Entity]RepositoryTests`.
- Follow the assertion and mock style in the nearest test file.

## Editing style

- Follow the nearest feature's existing pattern before introducing a new one.
- Preserve `// REFACTORED:` markers.
- Keep changes surgical and minimal.
- Add XML docs on new public APIs only when surrounding code already has them.

## Confidence gate

The pre-task research steps above are not optional and are not conditional on confidence level. Subjective confidence in training knowledge is not a substitute for running `codebase-researcher` and `official-docs-researcher`. If either step was skipped, stop and run it before proceeding.

Any factual claim about what the code currently does must be verified by reading the source. This includes — but is not limited to — what fields a class exposes, what a method contains, which properties are mapped, how many items exist in a collection, and what a test asserts. Memory, conversation summaries, and earlier reads are starting points, not ground truth. If you have not read the relevant code in the current session, read it before stating anything about it.

When codebase conventions and official best practices conflict, do not silently pick one or silently compromise. Name the tension, explain the tradeoff clearly, and resolve it with the user before writing any code.

### Mandatory stop-and-ask triggers

Stop immediately and ask before proceeding in any of these four situations:

1. **Data deletion or irreversible state change** — any task that deletes records, drops columns, removes stored files, or produces state that cannot be rolled back. State the exact scope of what will be lost and wait for explicit confirmation.

2. **Ownership or auth boundary ambiguity** — when it is unclear which user role owns an entity, which users are permitted to perform an operation, or how an authorization check should be enforced. Do not infer ownership rules from surrounding code; ask.

3. **Two equally valid patterns exist in the codebase** — when two feature areas implement the same concern differently and both are legitimate (e.g. two valid validator styles, two outbox payload shapes). Name both patterns, state the tradeoff, and let the user choose which to follow.

4. **Feature area has no existing tests** — when the code being added or changed has no test coverage and the appropriate testing level (unit vs integration) is not obvious from context. Ask which level matches intent before writing any test.
