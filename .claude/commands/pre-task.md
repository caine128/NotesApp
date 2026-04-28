Run the mandatory pre-task research phase for this coding task:

$ARGUMENTS

## Phase 1 — Codebase research (always required)

Use the `codebase-researcher` subagent to explore the relevant feature area.
It must read all related files (handler, validator, entity, repository, controller, mappings, tests) and return a structured terrain summary including convention tensions, ambiguities, and a proposed implementation approach.

Do not proceed to Phase 2 until the terrain summary is complete.

## Phase 2 — Docs research (required if external frameworks are involved)

If the task involves any framework-specific behavior, NuGet package, SDK, external API, auth provider, or database-provider behavior, use the `official-docs-researcher` subagent now.

Pass it the specific topic — not the full task description.

Skip this phase only if the task is purely internal logic with no external dependencies.

## Phase 3 — Conflict resolution

Review both research summaries. If any convention tension or codebase-vs-best-practice conflict was flagged:
- Present it to the user clearly: what the codebase does, what official guidance says, what the tradeoff is
- State which direction you lean and why
- Wait for the user's decision before proceeding

Do not silently compromise. Do not proceed until all conflicts are resolved.

## Phase 4 — Clarification

If any ambiguity remains in the task description or the terrain, ask the user now.
Do not assume. Do not proceed until all questions are answered.

## Phase 5 — Implementation plan report

Present a structured report to the user:

**Implementation plan**
- Files to create or modify (list each with its role)
- Step-by-step order of changes
- Patterns and conventions that will be applied
- Any decisions already made and why
- Anything still pending user input

**Wait for explicit user approval before writing any code.**

Only begin implementation after the user confirms the plan.
