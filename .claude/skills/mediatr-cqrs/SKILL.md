---
name: mediatr-cqrs
description: MediatR CQRS patterns for commands, queries, notifications, and pipeline behaviors. Covers IRequest, IRequestHandler, IPipelineBehavior, assembly scanning, validation pipeline integration, and the thin-controller pattern used throughout the Application layer.
invocable: false
---

# MediatR CQRS Patterns

## When to Use This Skill

Use this skill when:
- Adding a new command or query to the Application layer
- Implementing `IPipelineBehavior<TRequest, TResponse>` for cross-cutting concerns
- Wiring up `INotificationHandler` for in-process events
- Registering handlers via assembly scanning
- Debugging MediatR dispatch or pipeline behavior ordering

## Core Principles

1. **One handler per request** — each `IRequest<T>` maps to exactly one `IRequestHandler<TRequest, TResponse>`
2. **Every command requires a paired validator** — see `fluent-validation` skill; no handler ships without one
3. **Pipeline behaviors for cross-cutting concerns** — validation, logging, exception handling belong in behaviors, not handlers
4. **Thin controllers** — controllers only call `_mediator.Send()` and map the result with `ToActionResult()`
5. **No nested dispatch** — never call `_mediator.Send()` from inside a handler

---

## Commands and Queries

### Defining a Command

```csharp
public sealed record CreateNoteCommand(string Title) : IRequest<Result<NoteDetailDto>>;
```

### Defining a Query

```csharp
public sealed record GetNoteQuery(Guid NoteId) : IRequest<Result<NoteDetailDto>>;
```

Use `sealed record` for both. Route IDs are written into the record by the controller before dispatch:

```csharp
command = command with { NoteId = id }; // route ID wins
```

### Implementing a Handler

Follow the command handler order from CLAUDE.md exactly:

```csharp
public sealed class UpdateNoteCommandHandler
    : IRequestHandler<UpdateNoteCommand, Result<NoteDetailDto>>
{
    private readonly INoteRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ISystemClock _clock;

    public UpdateNoteCommandHandler(
        INoteRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ISystemClock clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<NoteDetailDto>> Handle(
        UpdateNoteCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve user and time
        var userId = _currentUser.UserId;
        var now = _clock.UtcNow;

        // 2. Load entity untracked
        var note = await _repository.GetByIdAsync(request.NoteId, cancellationToken);

        // 3. Ownership / existence checks — return early on failure
        if (note is null)
            return Result.Fail<NoteDetailDto>(new NotFoundError("Note", request.NoteId));

        if (note.UserId != userId)
            return Result.Fail<NoteDetailDto>(new ForbiddenError("Note"));

        // 4. Apply domain methods in memory
        var domainResult = note.Update(request.Title, now);
        if (domainResult.IsFailure)
            return Result.Fail<NoteDetailDto>(domainResult.Error);

        // 5. Create outbox message
        var outbox = OutboxMessage.Create<Note, NoteUpdatedEvent>(note);

        // 6. Repository update
        _repository.Update(note);
        await _unitOfWork.AddOutboxMessageAsync(outbox, cancellationToken);

        // 7. Single SaveChanges
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok(NotesMappings.ToDetailDto(note));
    }
}
```

---

## Notifications (In-Process Events)

Use `INotification` only for in-process side effects that do **not** require durability. Durable domain events go through the outbox.

```csharp
public sealed record NoteViewedNotification(Guid NoteId, Guid UserId) : INotification;

public sealed class UpdateNoteViewMetricsHandler
    : INotificationHandler<NoteViewedNotification>
{
    public async Task Handle(NoteViewedNotification notification, CancellationToken ct)
    {
        // in-memory metrics update, cache invalidation, etc.
    }
}

// Publishing from a handler
await _mediator.Publish(new NoteViewedNotification(note.Id, userId), cancellationToken);
```

---

## Pipeline Behaviors

Behaviors execute in **registration order**. Wrap `next()` to intercept before and after:

```csharp
public sealed class ValidationPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
        {
            // Return Result.Fail — never throw ValidationException from a behavior
            var errors = failures.Select(f => f.ErrorMessage);
            return (TResponse)(object)Result.Fail(errors);
        }

        return await next();
    }
}
```

### Registering Behaviors

```csharp
// First registered = outermost wrapper (executes first)
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
```

---

## Assembly Scanning

```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(CreateNoteCommand).Assembly);
});

// Validators from the same assembly (see fluent-validation skill)
services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
```

---

## Controller Pattern

Controllers dispatch and translate only — never contain business logic:

```csharp
[ApiController]
[Route("api/notes")]
public sealed class NotesController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateNoteRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateNoteCommand(request.Title);
        var result = await _mediator.Send(command, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateNoteRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateNoteCommand(request.Title) with { NoteId = id };
        var result = await _mediator.Send(command, cancellationToken);
        return result.ToActionResult();
    }
}
```

---

## Codebase Conventions

| Convention | Detail |
|---|---|
| Handler file location | Same folder as the command/query |
| Validator file | Same folder, named `[Command]Validator.cs` |
| Return type | Always `Result<T>` (FluentResults) |
| Route ID override | `command = command with { Id = routeId }` before `Send` |
| No nested MediatR | Never call `_mediator.Send` from inside a handler |
| No exceptions for business errors | Use `Result.Fail(new TypedError(...))` |

---

## Anti-Patterns

```csharp
// DON'T: Business logic in the controller
[HttpPost]
public async Task<IActionResult> Create([FromBody] CreateNoteRequest req)
{
    if (string.IsNullOrEmpty(req.Title)) return BadRequest(); // belongs in validator
    var note = new Note(req.Title); // belongs in domain
    // ...
}

// DON'T: Nested MediatR dispatch
public async Task<Result<NoteDetailDto>> Handle(CreateNoteCommand request, CancellationToken ct)
{
    await _mediator.Send(new AuditCommand(request.Title)); // never nest
}

// DON'T: Throw instead of returning Result.Fail
public async Task<Result<NoteDetailDto>> Handle(GetNoteQuery request, CancellationToken ct)
{
    var note = await _repo.GetByIdAsync(request.NoteId, ct)
        ?? throw new NotFoundException(); // use Result.Fail instead
}

// DON'T: Skip the validator
// Every handler MUST have a paired AbstractValidator<TCommand> in the same folder.
```

---

## Resources

- **MediatR GitHub**: https://github.com/jbogard/MediatR
- **MediatR Wiki / Behaviors**: https://github.com/jbogard/MediatR/wiki/Behaviors
