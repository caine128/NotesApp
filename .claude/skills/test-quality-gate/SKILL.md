---
name: test-quality-gate
description: Post-implementation test quality audit for NotesApp handlers. Runs three passes in sequence: anti-pattern check (always), mock analysis (if >3 mocks), and pseudo-mutation gap analysis (if handler has branching logic). Specific to xUnit + Moq + FluentAssertions + Result<T> patterns used in this codebase.
invocable: false
---

# Test Quality Gate

## When to Apply

Run this skill after writing or modifying tests for a handler. Apply all passes that match the handler's complexity:

| Condition | Pass to run |
|-----------|-------------|
| Any new tests written | Pass 1 — Anti-pattern check (always) |
| Test file has 3 or more mocks | Pass 2 — Mock analysis |
| Handler has multiple early-return branches, domain method calls, or outbox logic | Pass 3 — Gap analysis |

**Do not run on integration tests in `NotesApp.Api.IntegrationTests`** — the anti-pattern calibration below is tuned for unit tests in `NotesApp.Application.Tests` and `NotesApp.Worker.Tests`. Integration tests legitimately make multiple method calls and use external state.

## Human-in-the-Loop Trigger

Stop and surface to the user — do not continue to the next task — if any of the following are found:

- A test has **no assertions** (assertion-free test)
- A **Critical mutation survives** in business logic (ownership check, domain method, outbox creation, save)
- A mock setup is **dead for every test** in the file (the setup is always unreachable — this is a structural problem)

For High and Moderate findings, report them and let the user decide whether to fix now or log for later.

---

## Pass 1 — Anti-Pattern Check (Always)

Read each test method and check for the following. Report severity and exact location for each finding.

### Critical — False Confidence (stop and surface)

**Assertion-free test**: A test method with no `Should()`, `Assert`, or `Verify()` call.
```csharp
// BAD — this test can never fail
[Fact]
public async Task Handle_WhenNoteNotFound_DoesNothing()
{
    await sut.Handle(command, CancellationToken.None);
    // no assertions
}
```

**Swallowed exception**: A try/catch that catches and does not rethrow or assert.
```csharp
// BAD — failure is hidden
try { await sut.Handle(command, ct); }
catch { /* ignored */ }
```

**Always-true assertion**: Asserting a literal or a condition that cannot be false.
```csharp
result.IsSuccess.Should().Be(true || false); // always true
```

### High — Weak Verification

**`IsFailed` without error type**: When a handler has multiple distinct early-return paths (null check, ownership check, domain method failure), asserting only `IsFailed` does not distinguish which path was taken. A mutation removing one guard may not break the test if another guard or the domain method also returns failure.

```csharp
// WEAK — passes even if wrong error path triggered
result.IsFailed.Should().BeTrue();

// BETTER — asserts the specific failure type
result.Errors.Should().ContainSingle(e => e is NotFoundError);

// OR — asserts that no side effects occurred
_repositoryMock.Verify(x => x.Update(It.IsAny<Note>()), Times.Never);
```

Flag this pattern whenever the handler being tested has two or more failure paths that both return `Result.Fail`.

**Clock assertion with loose tolerance when `ISystemClock` is injectable**: This codebase injects `ISystemClock` — the test controls the clock exactly. A `BeCloseTo` with a tolerance wider than 1 second indicates the test is not using a fixed clock value.

```csharp
// WEAK — 60-second tolerance when clock is mockable
dto.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

// CORRECT — fix the clock in the mock, then assert exact value
_clockMock.Setup(x => x.UtcNow).Returns(_fixedUtcNow);
dto.UpdatedAtUtc.Should().Be(_fixedUtcNow);
```

**`Verify(Times.Once)` without asserting the argument**: Verifying a repository call happened without checking what was passed allows mutations that change the entity's state before saving to go undetected.

```csharp
// WEAK — confirms save happened but not what was saved
_repositoryMock.Verify(x => x.Update(It.IsAny<Note>()), Times.Once);

// STRONGER — at minimum capture and check key fields
_repositoryMock.Verify(
    x => x.Update(It.Is<Note>(n => n.Title == command.Title && n.UserId == userId)),
    Times.Once);
```

### Medium — Maintainability

**Naming inconsistency**: Application.Tests uses two naming styles. `Handle_with_valid_command_creates_note` (snake_case conditions) and `Handle_WhenNoteDoesNotExist_ReturnsNotFound` (PascalCase conditions). New tests should follow the PascalCase form: `Handle_When[Condition]_[ExpectedOutcome]`.

**`It.IsAny<CancellationToken>()` where a specific token is available**: If the test creates a `CancellationToken`, passing `It.IsAny` in mock setups means cancellation propagation is not verified.

### Low — Style

**Magic values without context**: Unexplained GUIDs, strings, or numbers in assertions that are not tied to the arrange data.

---

## Pass 2 — Mock Analysis (if test file has 3+ mocks)

For each mock setup in the test file, trace whether it is actually reached for each test method that uses it.

### The `CreateSut()` pattern — the main risk in this codebase

Many handler tests use a shared `CreateSut()` factory:

```csharp
private UpdateNoteCommandHandler CreateSut()
{
    _currentUserServiceMock
        .Setup(x => x.GetUserIdAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(_currentUserId);

    _clockMock
        .Setup(x => x.UtcNow)
        .Returns(_utcNow);

    _repositoryMock
        .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((Note?)null);

    return new UpdateNoteCommandHandler(...);
}
```

For each test that calls `CreateSut()`, trace the handler's execution path and classify each setup as:

| Classification | Meaning | Action |
|---------------|---------|--------|
| **Used** | The mock is called for this test's inputs | No action |
| **Unreachable** | Handler returns early before this mock is called | Flag as Moderate — consider per-test setup |
| **Unused** | Mock is set up but handler never calls it for any test | Flag as High — remove or scope to tests that need it |
| **Redundant** | Identical setup duplicated across tests | Flag as Low |

### Framework mocks that should be real

`ILogger<T>` and `IOptions<T>` should use real instances unless the test explicitly asserts log output or option values. Mocking them adds noise without benefit.

```csharp
// UNNECESSARY — ILogger doesn't affect handler logic
_loggerMock.Setup(x => x.Log(...));  // remove this

// USE REAL INSTANCE INSTEAD
var logger = NullLogger<UpdateNoteCommandHandler>.Instance;
```

### Tracing the handler order

When analysing reachability, follow the standard NotesApp command handler order:

1. `ICurrentUserService.GetUserIdAsync` — always reached
2. Repository `GetByIdAsync` — always reached (unless user service throws)
3. Null / ownership check — early return if entity not found or wrong user
4. `ISystemClock.UtcNow` — only reached if ownership check passes
5. Domain method — only reached if checks pass
6. Outbox creation — only reached if domain method succeeds
7. `Repository.Update` / `AddAsync` — only reached if no failure
8. `UnitOfWork.SaveChangesAsync` — always last, only if no failure

Any setup for steps 4–8 is **unreachable** in tests that exercise early-return paths at steps 3.

---

## Pass 3 — Gap Analysis via Pseudo-Mutation (handlers with branching)

For each branch point in the handler, reason about whether the existing tests would catch a mutation. Classify as **Killed**, **Survived**, **No coverage**, or **Equivalent**.

### Mutation targets specific to this codebase

**Ownership / existence guard removal**
```csharp
// Original
if (note is null || note.UserId != userId)
    return Result.Fail(new NotFoundError());

// Mutation A — remove null check
if (note.UserId != userId)  // NullReferenceException if note is null

// Mutation B — flip || to &&
if (note is null && note.UserId != userId)  // short-circuits on null, skips ownership
```
Tests that cover this need to assert `result.Errors.ContainSingle(e => e is NotFoundError)`, not just `result.IsFailed`. Otherwise Mutation B survives.

**Soft-delete guard removal**
```csharp
// Original
if (note.IsDeleted)
    return Result.Fail(new NotFoundError());

// Mutation — remove this block entirely
```
If the domain method also guards against deleted state and returns `DomainFailure`, a test asserting only `IsFailed` will not catch this mutation. The test must either assert `NotFoundError` specifically or verify that the domain method was not called.

**Outbox message creation removal**
```csharp
// Original
var outboxMessage = OutboxMessage.Create<Note, NoteUpdatedEvent>(...);
_outboxRepository.Add(outboxMessage);

// Mutation — remove these two lines
```
If no test verifies `_outboxRepositoryMock.Verify(x => x.Add(...), Times.Once)`, this mutation survives silently. All command handlers that create outbox messages should have a test that verifies the outbox write.

**`SaveChangesAsync` call removal**
```csharp
// Mutation — remove the save call
await _unitOfWork.SaveChangesAsync(ct);
```
Verify: `_unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once)` must exist in every success-path test.

**Return value mutation**
```csharp
// Original
return Result.Ok(NotesMappings.ToDetailDto(note));

// Mutation — return empty/default DTO
return Result.Ok(new NoteDetailDto());
```
Tests that only assert `result.IsSuccess` will not catch this. At minimum assert one identifying field: `result.Value.Id.Should().Be(note.Id)`.

### Reporting format

For each survived mutation, report:
- Location (file + method + line)
- The mutation applied
- Which test covers this code and why its assertions miss it
- The minimal assertion or new test that would kill it

Group by priority: ownership/security logic first, domain logic second, outbox/persistence third, mapping last.

---

## What Good Looks Like — Reference Patterns for This Codebase

### Full success-path test for a command handler
```csharp
[Fact]
public async Task Handle_WhenCommandIsValid_UpdatesNoteAndReturnsDto()
{
    // Arrange
    var note = Note.Create(userId: _currentUserId, title: "Original");
    _repositoryMock
        .Setup(x => x.GetByIdAsync(command.NoteId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(note);

    var sut = CreateSut();

    // Act
    var result = await sut.Handle(command, CancellationToken.None);

    // Assert — result shape
    result.IsSuccess.Should().BeTrue();
    result.Value.Title.Should().Be(command.Title);
    result.Value.UpdatedAtUtc.Should().Be(_fixedUtcNow);  // exact, not BeCloseTo

    // Assert — side effects
    _repositoryMock.Verify(
        x => x.Update(It.Is<Note>(n => n.Title == command.Title)),
        Times.Once);
    _outboxMock.Verify(
        x => x.Add(It.IsAny<OutboxMessage>()),
        Times.Once);
    _unitOfWorkMock.Verify(
        x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
        Times.Once);
}
```

### Ownership / existence failure test
```csharp
[Fact]
public async Task Handle_WhenNoteBelongsToAnotherUser_ReturnsNotFoundAndDoesNotSave()
{
    var otherUsersNote = Note.Create(userId: Guid.NewGuid(), title: "Other");
    _repositoryMock
        .Setup(x => x.GetByIdAsync(command.NoteId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(otherUsersNote);

    var result = await CreateSut().Handle(command, CancellationToken.None);

    // Assert error type — not just IsFailed
    result.HasError<NotFoundError>().Should().BeTrue();

    // Assert no side effects
    _repositoryMock.Verify(x => x.Update(It.IsAny<Note>()), Times.Never);
    _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
}
```
