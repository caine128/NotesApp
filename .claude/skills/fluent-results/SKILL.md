---
name: fluent-results
description: FluentResults patterns for explicit error handling across Domain, Application, and Api layers. Covers Result<T> creation, typed error classes, result chaining, merging, and mapping to HTTP responses via ToActionResult(). Includes the DomainResult-to-Result conversion pattern used throughout this codebase.
invocable: false
---

# FluentResults Patterns

## When to Use This Skill

Use this skill when:
- Returning results from Application handlers (`Result<T>`)
- Creating typed error classes for domain and application failures
- Mapping handler results to HTTP responses in controllers (`ToActionResult()`)
- Chaining or composing multiple results

## Layer Responsibilities

| Layer | Return Type | How |
|---|---|---|
| Domain methods | `DomainResult<T>` / `DomainResult` | Project-specific type — converted in handlers |
| Application handlers | `Result<T>` | Always FluentResults `Result<T>` |
| Api controllers | `IActionResult` | `result.ToActionResult()` |

---

## Creating Results

```csharp
// Success with value
return Result.Ok(NotesMappings.ToDetailDto(note));

// Success without value (e.g., delete handler)
return Result.Ok();

// Failure with typed error (preferred — maps to a specific HTTP status)
return Result.Fail<NoteDetailDto>(new NotFoundError("Note", request.NoteId));

// Failure with plain message (use only when no specific HTTP mapping is needed)
return Result.Fail<NoteDetailDto>("Operation failed.");

// Converting from a DomainResult (existing codebase helper pattern)
var domainResult = note.Update(request.Title, now);
if (domainResult.IsFailure)
    return Result.Fail<NoteDetailDto>(domainResult.Error);
```

---

## Typed Error Classes

Define typed errors so controllers and the framework can map them to HTTP status codes without string matching:

```csharp
public sealed class NotFoundError : Error
{
    public NotFoundError(string entityName, Guid id)
        : base($"{entityName} with id '{id}' was not found.")
    {
        Metadata["EntityName"] = entityName;
        Metadata["EntityId"] = id.ToString();
    }
}

public sealed class ForbiddenError : Error
{
    public ForbiddenError(string resource)
        : base($"Access to '{resource}' is denied.")
    { }
}

public sealed class ConflictError : Error
{
    public ConflictError(string message) : base(message) { }
}

public sealed class ValidationError : Error
{
    public ValidationError(IEnumerable<string> messages)
        : base(string.Join("; ", messages))
    { }
}
```

---

## Checking Results

```csharp
var result = await _mediator.Send(command, cancellationToken);

if (result.IsSuccess)
{
    var dto = result.Value;
}

if (result.IsFailed)
{
    var messages = result.Errors.Select(e => e.Message);
}

// Check for a specific error type
if (result.HasError<NotFoundError>())
{
    return NotFound();
}
```

---

## Mapping to HTTP Responses (Controllers)

```csharp
// GET — 200 Ok(dto) on success, mapped error on failure
[HttpGet("{id:guid}")]
public async Task<IActionResult> Get(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new GetNoteQuery(id), ct);
    return result.ToActionResult();
}

// POST — 201 Created on success
[HttpPost]
public async Task<IActionResult> Create(
    [FromBody] CreateNoteRequest request,
    CancellationToken ct)
{
    var command = new CreateNoteCommand(request.Title);
    var result = await _mediator.Send(command, ct);

    return result.IsSuccess
        ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
        : result.ToActionResult();
}

// PUT — 200 Ok(dto) on success
[HttpPut("{id:guid}")]
public async Task<IActionResult> Update(
    Guid id,
    [FromBody] UpdateNoteRequest request,
    CancellationToken ct)
{
    var command = new UpdateNoteCommand(request.Title) with { NoteId = id };
    var result = await _mediator.Send(command, ct);
    return result.ToActionResult();
}

// DELETE — 204 NoContent on success
[HttpDelete("{id:guid}")]
public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new DeleteNoteCommand(id), ct);
    return result.IsSuccess ? NoContent() : result.ToActionResult();
}
```

### How `ToActionResult()` maps typed errors

`ToActionResult()` from `FluentResults.Extensions.AspNetCore` maps errors to Problem Details responses. Register the error-to-status mapping once at startup:

```csharp
services.AddFluentResultsForAspNetCore(cfg =>
{
    cfg.Map<NotFoundError>(HttpStatusCode.NotFound);
    cfg.Map<ForbiddenError>(HttpStatusCode.Forbidden);
    cfg.Map<ConflictError>(HttpStatusCode.Conflict);
    cfg.Map<ValidationError>(HttpStatusCode.UnprocessableEntity);
});
```

Without this registration, `ToActionResult()` defaults to 400 for all failures.

---

## Chaining Results

```csharp
// Map transforms the value inside a successful result — no-op on failure
var dtoResult = noteResult.Map(note => NotesMappings.ToDetailDto(note));

// Bind chains an operation that itself returns a Result
var result = GetNote(noteId)
    .Bind(note => CheckOwnership(note, userId))
    .Bind(note => ApplyUpdate(note, request));
```

---

## Merging Results

When multiple independent checks can each fail, collect all errors before returning:

```csharp
var titleCheck = ValidateTitle(command.Title);
var userCheck  = ValidateUserId(command.UserId);

var merged = Result.Merge(titleCheck, userCheck);
if (merged.IsFailed)
    return merged.ToResult<NoteDetailDto>();
```

---

## Anti-Patterns

```csharp
// DON'T: Throw exceptions for expected business failures
var note = await _repo.GetByIdAsync(request.NoteId, ct)
    ?? throw new NotFoundException(); // use Result.Fail(new NotFoundError(...))

// DON'T: Access .Value without checking IsSuccess
var dto = result.Value; // throws InvalidOperationException if failed

// DON'T: Use plain string errors when the controller needs to distinguish them
return Result.Fail("Not found"); // use Result.Fail(new NotFoundError(...))

// DON'T: Return domain-specific errors directly from the controller layer
// Controllers should not know about DomainResult — handlers convert before returning.

// DO: Always use typed errors for any result that maps to a specific HTTP status
return Result.Fail<NoteDetailDto>(new NotFoundError("Note", request.NoteId));
```

---

## Resources

- **FluentResults GitHub**: https://github.com/altmann/FluentResults
- **FluentResults.Extensions.AspNetCore**: https://github.com/altmann/FluentResults#aspnet-core-integration
