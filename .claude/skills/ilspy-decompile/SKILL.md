---
name: ilspy-decompile
description: Understand implementation details of .NET code by decompiling assemblies. Use when you want to see how a .NET API works internally, inspect NuGet package source, view framework implementation, or verify performance characteristics of compiled output via IL analysis.
allowed-tools: Bash(dnx:*), Bash(ilspycmd:*), Bash(dotnet:*)
---

# .NET Assembly Decompilation with ILSpy

Use this skill to understand how .NET code works internally and to verify that compiled output matches performance expectations.

## Prerequisites

- .NET SDK installed
- ILSpy command-line tool available via one of the following:
  - `dnx ilspycmd` (if available in your SDK or runtime)
  - `dotnet tool install --global ilspycmd`

Both forms are shown below. Use the one that works in your environment.

> Note: ILSpyCmd options may vary slightly by version.  
> Always verify supported flags with `ilspycmd -h`.

## Quick start

```bash
# Decompile an assembly to stdout
ilspycmd MyLibrary.dll
# or
dnx ilspycmd MyLibrary.dll

# Decompile to an output folder
ilspycmd -o output-folder MyLibrary.dll
```

## Common .NET Assembly Locations

### NuGet packages

```bash
~/.nuget/packages/<package-name>/<version>/lib/<tfm>/
```

### .NET runtime libraries

```bash
dotnet --list-runtimes
```

### .NET SDK reference assemblies

```bash
dotnet --list-sdks
```

> Reference assemblies do not contain implementations.

### Project build output

```bash
./bin/Debug/net10.0/<AssemblyName>.dll
./bin/Release/net10.0/publish/<AssemblyName>.dll
```

## Core workflow

1. Identify what you want to understand or verify
2. Build the project (`dotnet build`)
3. Locate the assembly in `bin/`
4. Decompile the target type or method
5. Inspect IL or C# output for correctness and performance

## Commands

### Basic decompilation

```bash
ilspycmd MyLibrary.dll
ilspycmd -o ./decompiled MyLibrary.dll
ilspycmd -p -o ./project MyLibrary.dll
```

### Targeted decompilation

```bash
ilspycmd -t Namespace.ClassName MyLibrary.dll
ilspycmd -lv CSharp12_0 MyLibrary.dll
```

### View IL code

```bash
ilspycmd -il MyLibrary.dll
ilspycmd -il -t Namespace.ClassName MyLibrary.dll
```

---

## Proactive Performance Analysis

**When to run IL checks automatically:**

- After implementing a `readonly struct`, `sealed record`, or value object — verify no defensive copies or box instructions appear
- After adding a hot-path method (called in a tight loop, per-request, or in a background worker) — confirm the JIT can inline and devirtualize
- After using `Span<T>`, `Memory<T>`, or `stackalloc` — confirm no unexpected heap allocation
- After implementing an async method — inspect the generated state machine for unnecessary captures
- When a performance benchmark shows unexpected allocations or latency

### IL patterns that signal a performance problem

| IL instruction | What it means | Fix |
|---|---|---|
| `box` | Value type being heap-allocated | Return/accept as `ref`, use `readonly struct`, check interface cast |
| `callvirt` on a sealed type | JIT cannot devirtualize | Seal the class or use a concrete type reference |
| `newobj` inside a loop | Heap allocation per iteration | Reuse instances, use `ArrayPool<T>`, use `stackalloc` |
| `ldfld` on a non-readonly struct | Defensive copy being made | Add `readonly` to the struct |
| Excessive `stloc`/`ldloc` in async method | State machine capturing too many variables | Minimize captured variables; extract async local functions |

### Workflow example: verify a readonly struct has no defensive copies

```bash
# 1. Build
dotnet build src/NotesApp.Domain

# 2. Decompile with IL output, targeting the specific type
ilspycmd -il -t NotesApp.Domain.SomeValueObject \
    src/NotesApp.Domain/bin/Debug/net10.0/NotesApp.Domain.dll

# 3. Search the IL output for box instructions
# If box appears before a method call on the struct, a defensive copy is happening
```

### Workflow example: check async state machine captures

```bash
# Decompile the handler to C# to inspect the compiler-generated state machine
ilspycmd -t NotesApp.Application.Features.Notes.CreateNoteCommandHandler \
    src/NotesApp.Application/bin/Debug/net10.0/NotesApp.Application.dll
```

Look for: fields on the generated `<Handle>d__N` state machine class. Each field is a captured variable that lives on the heap for the lifetime of the async operation. Unnecessary captures (like large objects or services that are only needed at the start) should be extracted before the first `await`.

---

## Notes on modern .NET builds

- ReadyToRun images may reduce IL readability — use Debug builds for analysis
- Trimmed or AOT builds may omit code
- Always prefer non-trimmed Debug builds when doing performance analysis

## Legal note

Decompiling assemblies may be subject to license restrictions.
