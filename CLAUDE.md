# CLAUDE.md

Apply these rules to the whole NotesApp repo.
See @.claude/research-policy.md

## Core rule
Follow the existing NotesApp pattern before introducing a new one. Inspect nearby files in the same feature first.

When a task involves framework-specific behavior, SDKs, external APIs, NuGet packages, authentication providers, database-provider behavior, or any third-party package with official documentation, use the `official-docs-researcher` subagent before recommending or implementing anything.

If Context7 or another current-doc MCP source is available, use it through the research subagent for library/framework documentation lookup. The research subagent must still prefer creator-owned official documentation when it is available and relevant.

Do not propose an approach unless you are fully confident it is the best, or clearly one of the best, options after:
1. checking the latest official docs from the package/framework creator,
2. comparing that guidance against the existing NotesApp codebase,
3. choosing the most coherent high-quality solution for this repo.

If official docs discourage an approach, do not recommend it.
If uncertainty remains, state the uncertainty explicitly instead of guessing.

## Confidence gate
Do not propose or implement a solution unless you are 100% confident it is the best solution, or clearly one of the best solutions, for this repo.
That confidence must be grounded in all three:
- current official documentation and best practices
- the existing NotesApp codebase and local feature patterns
- coherence with the current architecture, conventions, and style

If that confidence is missing, do not guess and do not present assumptions as recommendations. Inspect more code first. If uncertainty still remains, say exactly what is uncertain and ask only the minimum necessary question.

## Architecture
- Keep Clean Architecture boundaries intact.
- Domain: entities, invariants, domain methods, domain results. No framework logic.
- Application: MediatR commands/queries, validators, mappings, use-case orchestration.
- Infrastructure: EF Core, repositories, current-user/time/storage/notification implementations.
- API: thin controllers only.
- Worker: outbox + reminder processing.

## Request/handler conventions
- Application handlers return `FluentResults.Result<T>`.
- Domain entities return `DomainResult` / `DomainResult<T>`.
- Use the existing conversion helpers instead of inventing new result shapes.
- Validators live in Application and are executed by the MediatR validation pipeline.

## Command handler pattern
For update/delete-style handlers, preserve this order:
1. Resolve current user and current UTC time through abstractions.
2. Load the entity **untracked**.
3. Perform ownership / existence / validation checks and return early if needed.
4. Apply domain methods in memory.
5. Create the outbox message.
6. Only then attach with `Repository.Update(...)` or add with `AddAsync(...)`.
7. Call `UnitOfWork.SaveChangesAsync(...)` once.

Do not attach tracked entities before early-return checks pass.

## Persistence / infra rules
- Prefer repository methods already present in the feature.
- Use `AsNoTracking` / untracked retrieval by default in handlers.
- Respect soft delete, versioning, and row-version concurrency patterns already in place.
- Use `ISystemClock` and `ICurrentUserService` in application code instead of ad hoc time/user access.

## Outbox / worker
- Never use magic strings for domain events when an existing enum exists.
- Use `OutboxMessage.Create<TAggregate, TEvent>()`.
- Use `OutboxPayloadBuilder` where a payload builder already exists.
- Keep entity change + outbox persistence in the same save boundary.

## API rules
- Controllers should only translate HTTP to MediatR requests and return `ToActionResult()` / `CreatedAtAction()` / `NoContent()`.
- Route IDs are the source of truth: overwrite body IDs from route values.
- Preserve existing auth / policy / debug-device-provisioning behavior when touching sync or secured endpoints.

## DTO / mapping rules
- Keep DTO naming consistent: `Detail`, `Summary`, `Overview`, `Sync*`.
- Put mapping logic in the existing static `*Mappings` files, not inside controllers or handlers.

## Sync rules
- Do not refactor sync handlers into nested command dispatch unless the codebase already does so.
- Preserve current sync semantics: per-item conflict results, version checks, delete-wins behavior, device ownership checks in handlers, and direct orchestration inside sync handlers.
- Subtasks use fractional-index `Position` strings; do not replace with integer ordering.

## Domain-specific repo facts to preserve
- Notes are block-based. Do not reintroduce note content back onto `Note`.
- `MeetingLink` on tasks is a plain nullable string, normalized by trimming; do not force it into a `Uri` type unless the codebase is changed globally.

## Tests
- Add or update tests for every meaningful backend change.
- Follow existing test naming and style.
- Prefer the same testing level already used in the area you touch (unit vs integration vs repo-backed handler tests).

## Editing style
- Keep changes surgical and minimal.
- Reuse existing naming, error-code style, logging style, and comment style.
- Preserve `REFACTORED:` markers when editing areas that already use them.
- Add XML docs for new public APIs when consistent with surrounding code.

## When uncertain
Do not guess. Inspect the relevant feature’s neighboring files and follow the local convention.
