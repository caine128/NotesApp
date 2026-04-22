# Official-doc research policy

Use official docs first for:
- ASP.NET Core
- Entity Framework Core
- MediatR
- FluentValidation
- FluentResults
- Azure SDKs
- Microsoft Entra / MSAL
- SQL Server provider behavior
- React Native
- any external API, SDK, or third-party package with official documentation

Research workflow:
1. Read the latest official documentation from the package/framework creator first.
2. Prefer creator docs and official examples over blogs or forum posts.
3. Identify:
   - recommended patterns,
   - discouraged patterns,
   - deprecated APIs,
   - current examples,
   - caveats relevant to this task.
4. Then inspect the nearest NotesApp code paths already solving similar problems.
5. Recommend the “golden middle”:
   - doc-compliant,
   - coherent with existing NotesApp architecture,
   - minimal disruption,
   - high maintainability.
6. Do not blindly import generic best practices that fight the codebase.
7. Do not guess. If confidence is not high, say so clearly.

Required output from research:
- official-doc summary
- discouraged/avoid list
- closest existing repo pattern
- recommended repo-coherent approach
- confidence level