# Official-doc research policy

Use docs-first research for:
- ASP.NET Core
- Entity Framework Core
- MediatR
- FluentValidation
- FluentResults
- Azure SDKs
- Microsoft Entra / MSAL
- SQL Server provider behavior
- React Native
- any external API, SDK, MCP tool, or third-party package with official documentation

Priority order:
1. Creator-owned official documentation
2. Creator-owned official examples / reference docs / GitHub docs
3. Current-doc tooling such as Context7, when available
4. Existing NotesApp code patterns

Research workflow:
1. Identify the exact framework, package, SDK, API, or provider involved.
2. Read the latest creator-owned official documentation first.
3. Use Context7 or similar current-doc tooling when available to quickly gather current examples and version-sensitive guidance.
4. Identify:
   - recommended patterns
   - discouraged patterns
   - deprecated APIs
   - current examples
   - caveats relevant to this task
5. Inspect the nearest NotesApp code paths already solving similar problems.
6. Recommend the golden middle:
   - doc-compliant
   - coherent with existing NotesApp architecture
   - minimal disruption
   - high maintainability
7. Do not blindly import generic best practices that fight the existing codebase.
8. Do not guess. If confidence is not high, say so clearly.

Required output from research:
- official guidance
- avoid / discouraged list
- closest existing NotesApp pattern
- recommended repo-coherent approach
- confidence
- concise implementation sketch only if confidence is high

Important:
- The research result should be concise and distilled.
- Do not dump long documentation into the main thread.
- The goal is to save main-context tokens, not to move token waste into a subagent.