---
name: analyzing-dotnet-performance
description: On-demand performance anti-pattern scanner for NotesApp. Identifies async misuse, EF Core query issues, LINQ over-materialization, and allocation hot spots. Categorises findings as Critical / Moderate / Info. Not a default step — triggered only when a handler or Worker method touches unbounded collections, or when slowness is suspected.
invocable: false
---

# Analyzing .NET Performance

## When to Apply

Run this skill when:
- A handler or repository method iterates or queries a **collection of unbounded size** (e.g., all notes for a user, all tasks for a day, all outbox messages)
- The **Worker outbox processing loop** is being modified
- A query or endpoint is **suspected to be slow** in production
- A **new sync handler** is written (sync handlers process collections by design)

Do **not** run this as a default step on every handler. A `GetById` with a single entity lookup has no performance surface area worth scanning.

## Human-in-the-Loop Trigger

Stop and surface to the user before continuing if any **Critical** finding is discovered. Critical findings represent deadlock risk or regression greater than 10×.

For **Moderate** findings, report them alongside the implementation. Let the user decide whether to fix now or defer. For **Info**, include in the report but do not block.

---

## Severity Classification

| Severity | Definition | Examples |
|----------|-----------|---------|
| **Critical** | Deadlock risk, crash risk, or >10× regression | `.Result` / `.Wait()` on async, `async void`, blocking the thread pool |
| **Moderate** | 2–10× improvement available | `ToList()` before `Where()`, N+1 query, missing `AsNoTracking()` on a tracked context |
| **Info** | Applicable improvement in non-critical path | Over-chained LINQ on small collections, minor allocation |

---

## Detection Categories

Scan only the categories relevant to the code being reviewed. Use the signal words below to determine which categories apply.

### Category 1 — Async Misuse (scan always for async code)

**Critical findings:**

`.Result` or `.GetAwaiter().GetResult()` on a `Task` in an async context:
```csharp
// CRITICAL — deadlock risk in ASP.NET Core
var note = _repository.GetByIdAsync(id, ct).Result;
var note = _repository.GetByIdAsync(id, ct).GetAwaiter().GetResult();
```

`async void` methods outside event handlers:
```csharp
// CRITICAL — exceptions are unobservable, crashes the process
public async void ProcessOutbox() { ... }
```

`Task.Delay(0)` or `Thread.Sleep` inside async code in the Worker:
```csharp
// CRITICAL — blocks thread pool thread
Thread.Sleep(100);
```

**Moderate findings:**

`await` inside a loop when requests can be parallelised:
```csharp
// MODERATE — sequential when parallel is possible
foreach (var message in outboxMessages)
{
    await ProcessMessageAsync(message, ct);  // one at a time
}

// BETTER — when messages are independent
await Task.WhenAll(outboxMessages.Select(m => ProcessMessageAsync(m, ct)));
// OR with bounded parallelism
await Parallel.ForEachAsync(outboxMessages,
    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
    async (m, token) => await ProcessMessageAsync(m, token));
```

Not passing `CancellationToken` to async methods that accept one:
```csharp
// MODERATE — token not propagated
var note = await _repository.GetByIdAsync(id);            // missing ct
var note = await _repository.GetByIdAsync(id, default);  // defeats cancellation
```

### Category 2 — EF Core Query Issues (scan any code touching repositories or DbContext)

**Critical findings:**

Loading a full collection and filtering in memory when the filter belongs in the query:
```csharp
// CRITICAL — loads every note for every user into memory
var notes = await _context.Notes.ToListAsync(ct);
var userNotes = notes.Where(n => n.UserId == userId).ToList();

// CORRECT — filter in SQL
var userNotes = await _context.Notes
    .Where(n => n.UserId == userId)
    .ToListAsync(ct);
```

**Moderate findings:**

N+1 pattern — querying inside a loop:
```csharp
// MODERATE — one query per task item
foreach (var task in tasks)
{
    var subtasks = await _context.Subtasks
        .Where(s => s.TaskId == task.Id)
        .ToListAsync(ct);
}

// BETTER — single query with Include or batch Where
var subtasks = await _context.Subtasks
    .Where(s => taskIds.Contains(s.TaskId))
    .ToListAsync(ct);
```

Calling `Count()` or `Any()` on a materialised list when the query is not yet executed:
```csharp
// MODERATE — loads all records to count them
var count = await _context.Notes.Where(...).ToListAsync(ct).Count;  // wrong

// BETTER
var count = await _context.Notes.Where(...).CountAsync(ct);
var hasAny = await _context.Notes.Where(...).AnyAsync(ct);
```

Using `First()` instead of `FirstOrDefault()` when the record may not exist:
```csharp
// MODERATE — throws InvalidOperationException, not a clean Result.Fail
var note = await _context.Notes.FirstAsync(n => n.Id == id, ct);

// CORRECT for this codebase
var note = await _context.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
if (note is null) return Result.Fail(new NotFoundError(...));
```

Missing `AsNoTracking()` on a query inside a class that does not inherit the global NoTracking setting (e.g., inside a raw DbContext that was not configured with `QueryTrackingBehavior.NoTracking`):
```csharp
// Check: does this DbContext configure NoTracking globally?
// If not, reads that do not need tracking waste memory on change tracker entries.
var note = await _context.Notes.FindAsync(id, ct);  // tracked!
// Should be:
var note = await _context.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
```

### Category 3 — LINQ Over-Materialisation (scan handlers that process lists)

**Moderate findings:**

`ToList()` before `Where()` or `OrderBy()`:
```csharp
// MODERATE — materialises entire collection before filtering
var active = notes.ToList().Where(n => !n.IsDeleted).OrderBy(n => n.CreatedAtUtc).ToList();

// CORRECT — single materialisation at the end
var active = notes.Where(n => !n.IsDeleted).OrderBy(n => n.CreatedAtUtc).ToList();
```

Multiple `ToList()` calls in a chain:
```csharp
// MODERATE — two allocations
var filtered = source.Where(...).ToList().Select(...).ToList();

// CORRECT — one allocation
var filtered = source.Where(...).Select(...).ToList();
```

`ToList()` immediately followed by `First()` or `SingleOrDefault()`:
```csharp
// MODERATE — loads all matching records to get one
var item = source.Where(x => x.Id == id).ToList().FirstOrDefault();

// CORRECT
var item = source.FirstOrDefault(x => x.Id == id);
```

**Info findings:**

LINQ over a small, bounded in-memory collection (e.g., a list of 3–10 items from a DTO). Note it but do not flag as actionable unless it is in a hot loop.

### Category 4 — Memory and Allocation (scan Worker and sync handlers)

**Moderate findings:**

`string` concatenation inside a loop:
```csharp
// MODERATE — O(n²) allocations
var result = "";
foreach (var item in items)
    result += item.ToString();  // new string every iteration

// BETTER
var result = string.Join(", ", items.Select(i => i.ToString()));
// OR for complex cases
var sb = new StringBuilder();
foreach (var item in items) sb.Append(item);
var result = sb.ToString();
```

Boxing value types in generic collections where `IEnumerable<object>` is used:
```csharp
// Info — if Guid, int, or struct is stored as object
var values = new List<object> { noteId, userId };
```

Large object allocations inside the outbox processing loop per message:
```csharp
// Moderate — new allocation per message in a tight loop
foreach (var message in outboxMessages)
{
    var payload = JsonSerializer.Deserialize<NoteUpdatedPayload>(message.Payload);
    var handler = new NoteUpdatedEventHandler(...);  // allocated per message
    await handler.HandleAsync(payload, ct);
}
// Consider: resolve handlers from DI (singleton/scoped) rather than new-ing per message
```

---

## Reporting Format

Present findings as a structured list. Group by severity, then by file/method.

```
CRITICAL (action required before proceeding)
  ├─ File: OutboxProcessor.cs, method: ProcessBatchAsync, line 47
  │   Pattern: .Result on async call — deadlock risk in ASP.NET Core thread pool
  │   Fix: await _repository.GetPendingAsync(ct) instead of .Result

MODERATE (report to user, fix on their direction)
  ├─ File: NotesQueryHandler.cs, method: Handle, line 23
  │   Pattern: ToList() before Where() — materialises all notes before filtering
  │   Fix: Move .ToListAsync(ct) after the .Where() clause

INFO (noted, no immediate action)
  └─ File: TasksMappings.cs, method: ToSummaryList, line 8
      Pattern: LINQ chain over bounded list (max ~20 items) — acceptable
```

If no findings in a category, state that explicitly rather than omitting it. Zero findings is a valid and useful result.
