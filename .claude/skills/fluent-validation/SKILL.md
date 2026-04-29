---
name: fluent-validation
description: FluentValidation patterns for command and query validators. Covers AbstractValidator, rule chaining, conditional rules, cross-property validation, async rules, collection rules, error codes, and integration with the MediatR validation pipeline. Every handler must have a paired validator.
invocable: false
---

# FluentValidation Patterns

## When to Use This Skill

Use this skill when:
- Writing a validator for a new command or query (mandatory — no handler ships without one)
- Implementing cross-property or conditional validation rules
- Adding async validation that checks the database (e.g., uniqueness)
- Testing validators in isolation

## Core Principles

1. **Every handler has a paired validator** — no exceptions, enforced by the pipeline behavior
2. **Same folder as the command/query** — `CreateNoteCommandValidator.cs` lives next to `CreateNoteCommand.cs`
3. **Use `WithErrorCode`** on all business rules so API consumers can handle them programmatically
4. **Async rules only when genuinely needed** — database checks only; prefer synchronous for performance
5. **Never validate inside the handler** — that belongs here

---

## Basic Structure

```csharp
public sealed class CreateNoteCommandValidator : AbstractValidator<CreateNoteCommand>
{
    public CreateNoteCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
                .WithErrorCode("TITLE_REQUIRED")
                .WithMessage("Title is required.")
            .MaximumLength(200)
                .WithErrorCode("TITLE_TOO_LONG")
                .WithMessage("Title must not exceed 200 characters.");
    }
}
```

---

## Common Rule Builders

```csharp
// String rules
RuleFor(x => x.Name)
    .NotEmpty()
    .MinimumLength(2)
    .MaximumLength(100)
    .Matches(@"^[\w\s\-]+$").WithMessage("Name contains invalid characters.");

// Numeric rules
RuleFor(x => x.Priority)
    .InclusiveBetween(1, 5).WithMessage("Priority must be between 1 and 5.");

// GUID / ID rules
RuleFor(x => x.NoteId)
    .NotEmpty().WithMessage("NoteId is required.");

// Enum validation
RuleFor(x => x.Status)
    .IsInEnum().WithMessage("Invalid status value.");

// Nullable — only validate when a value is provided
RuleFor(x => x.DueDate)
    .GreaterThan(DateTimeOffset.UtcNow)
    .When(x => x.DueDate.HasValue);
```

---

## Conditional Rules

```csharp
public sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        // Validate MeetingLink only when it's provided
        RuleFor(x => x.MeetingLink)
            .MaximumLength(2000)
            .When(x => x.MeetingLink is not null);

        // Due date required only for non-recurring tasks
        RuleFor(x => x.DueDate)
            .NotNull()
                .WithErrorCode("DUE_DATE_REQUIRED")
                .WithMessage("Non-recurring tasks require a due date.")
            .When(x => !x.IsRecurring);
    }
}
```

---

## Cross-Property Validation

```csharp
public sealed class DateRangeValidator : AbstractValidator<GetNotesQuery>
{
    public DateRangeValidator()
    {
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
                .WithErrorCode("INVALID_DATE_RANGE")
                .WithMessage("End date must be after start date.")
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue);
    }
}
```

---

## Async Rules (Database Checks)

Only use async rules when you need to query external state. The validator is resolved from DI, so repositories can be injected:

```csharp
public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator(ICategoryRepository repository)
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .MustAsync(async (name, ct) =>
                !await repository.ExistsWithNameAsync(name, ct))
                .WithErrorCode("CATEGORY_NAME_TAKEN")
                .WithMessage("A category with this name already exists.");
    }
}
```

---

## Collection Rules

```csharp
// Validate each item in a collection
RuleForEach(x => x.TagIds)
    .NotEmpty().WithMessage("Tag ID must not be empty.");

// Validate the collection itself
RuleFor(x => x.TagIds)
    .Must(ids => ids.Distinct().Count() == ids.Count)
        .WithErrorCode("DUPLICATE_TAGS")
        .WithMessage("Tags must not contain duplicates.")
    .When(x => x.TagIds is { Count: > 0 });
```

---

## Child Validators

```csharp
public sealed class AddressValidator : AbstractValidator<AddressDto>
{
    public AddressValidator()
    {
        RuleFor(x => x.Street).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
    }
}

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.ShippingAddress)
            .NotNull()
            .SetValidator(new AddressValidator());
    }
}
```

---

## Registration

```csharp
// Register all validators discovered in the Application assembly
services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
```

Validators with constructor parameters (e.g., repository dependencies) are resolved from DI automatically.

---

## Testing Validators

Validators are plain classes — test them directly, no mocking of MediatR required:

```csharp
public class CreateNoteCommandValidatorTests
{
    private readonly CreateNoteCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes()
    {
        var command = new CreateNoteCommand("My Note");
        var result = await _validator.ValidateAsync(command);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyTitle_Fails()
    {
        var command = new CreateNoteCommand(string.Empty);
        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == nameof(command.Title) &&
            e.ErrorCode == "TITLE_REQUIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_BlankTitle_Fails(string? title)
    {
        var command = new CreateNoteCommand(title!);
        var result = await _validator.ValidateAsync(command);
        result.IsValid.Should().BeFalse();
    }
}
```

---

## Anti-Patterns

```csharp
// DON'T: Validation logic in the handler
public async Task<Result<NoteDetailDto>> Handle(CreateNoteCommand request, CancellationToken ct)
{
    if (string.IsNullOrEmpty(request.Title))
        return Result.Fail("Title is required"); // belongs in the validator
}

// DON'T: Async rule for something that can be checked synchronously
RuleFor(x => x.Title)
    .MustAsync((title, ct) => Task.FromResult(title.Length <= 200)); // just use MaximumLength

// DON'T: Missing error codes on business rules
RuleFor(x => x.Title)
    .NotEmpty().WithMessage("Required"); // add .WithErrorCode("TITLE_REQUIRED")

// DON'T: Catching ValidationException — the pipeline behavior handles it
try { await _mediator.Send(command); }
catch (ValidationException) { } // wrong layer
```

---

## Resources

- **FluentValidation Docs**: https://docs.fluentvalidation.net
- **Built-in Validators**: https://docs.fluentvalidation.net/en/latest/built-in-validators.html
- **Async Validation**: https://docs.fluentvalidation.net/en/latest/async.html
