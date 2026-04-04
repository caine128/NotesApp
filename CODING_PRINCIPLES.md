# NotesApp Coding Principles

This document outlines the core coding principles and conventions that guide development in the NotesApp project. All developers should be familiar with these principles before contributing code.

## 1. Code Quality & Framework Best Practices

- Enforce good practices consistently across the codebase
- Follow current online documentation for all frameworks and libraries used:
  - ASP.NET Core
  - Entity Framework Core
  - CQRS with MediatR
  - FluentValidation
  - FluentResults
  - React Native
- Verify consistency with existing codebase patterns before suggesting or implementing code
- When in doubt about framework-specific features, consult the official documentation rather than relying on training data or assumptions

**Why**: Production-grade applications require adherence to established best practices. Framework documentation evolves, and outdated patterns can introduce bugs or security vulnerabilities.

---

## 2. Entity Retrieval: Non-Tracking by Default

When retrieving entities from the database, **always use non-tracking queries** as the default approach:

- Retrieve entities untracked in all handlers (queries AND commands)
- Only explicitly attach tracked entities (via `Repository.Update()`) after all validations pass
- Return early on authorization/validation failures before attaching entities for modification

This prevents accidental persistence of entities when early returns occur due to validation failures, authorization checks, or business logic errors.

**See actual implementation**: 
- `NotesApp.Application/Notes/Commands/UpdateNote/UpdateNoteCommandHandler.cs` (lines 50-62: non-tracking retrieval with early returns)
- `NotesApp.Application/Notes/Commands/CreateNote/CreateNoteCommandHandler.cs` (lines 91-99: domain factory validation before persistence)

**Why**: Non-tracking queries improve performance and prevent subtle bugs from unintended change tracking. The pattern makes intent explicit: "I'm reading this, determining what to do, then either returning early OR explicitly attaching for modification."

---

## 3. Self-Documenting Code

Write code that is clear and expressive without requiring extensive comments:

- Method names, variable names, and class names must clearly express intent and purpose
- Avoid cryptic abbreviations; use full, meaningful names
- Add XML documentation comments (`///`) for all public APIs
- XML comments should explain **why** decisions were made, not just **what** the code does
- Log important state transitions and authorization decisions for observability

**See actual implementation**: 
- `NotesApp.Application/Notes/Commands/UpdateNote/UpdateNoteCommandHandler.cs` (class-level sealed declaration, method-level logging at lines 54-57, 117-120)
- `NotesApp.Domain/Entities/Note.cs` (XML documentation on factory methods)

**Why**: Self-documenting code is easier to maintain, reduces cognitive load when reviewing changes, and serves as the primary documentation for future developers.

---

## 4. Refactoring: Mark Changed Areas Clearly

When refactoring code, clearly mark all changed areas with descriptive comments. This allows reviewers and future developers to see what changed without reading the entire file.

Pattern: `// REFACTORED: [specific change description]`

**See actual implementation**: 
- Review git blame for recent refactorings to see the pattern in use
- Check pull request history for REFACTORED markers in command handlers

**Why**: Clear refactoring markers save time during code reviews and make the git history more readable. They signal intentional changes rather than casual modifications.

---

## 5. Verification & Uncertainty Approach

- **Never guess** or make assumptions about code consistency or correctness
- When uncertain about codebase patterns or architectural decisions, **ask for clarification**
- Always review uploaded codebase files to verify consistency before suggesting code
- Maintain 100% confidence in all suggestions—if you can't be certain, raise the question

**Process when uncertain**:
1. Identify the area of uncertainty clearly
2. Ask specific questions about the existing pattern
3. Request codebase context if needed
4. Only proceed once clarity is achieved

**Why**: Incorrect assumptions lead to inconsistent code, bugs, and technical debt. It's better to ask and verify once than to debug issues later.

---

## 6. Online Documentation Verification

For every framework and library-specific feature used:

- Verify against **current official documentation** (not training data)
- Make online calls to confirm best practices haven't changed
- Don't assume that training data represents current API behavior, validation patterns, or recommended approaches
- Check for breaking changes or deprecations

**Libraries to verify**:
- ASP.NET Core (Microsoft Docs)
- Entity Framework Core (Microsoft Docs)
- MediatR (GitHub Repository & NuGet)
- FluentValidation (GitHub Repository)
- FluentResults (GitHub Repository)
- React Native (Official Documentation)
- Microsoft Entra External ID (Microsoft Docs)
- Azure SDK libraries (Microsoft Docs)

**Why**: Framework documentation evolves rapidly. Outdated patterns from training data can result in deprecated APIs, security vulnerabilities, or performance issues. Online verification ensures we're using current best practices.

---

## 7. Clean Architecture Principles

NotesApp follows **Clean Architecture** with clear separation of concerns:

### Project Structure
- **Domain Layer** (`NotesApp.Domain`): Core business logic, entities, value objects, interfaces, event type enums
- **Application Layer** (`NotesApp.Application`): CQRS handlers, DTOs, validators, mappers, use cases
- **Infrastructure Layer** (`NotesApp.Infrastructure`): Database access, repositories, external services, identity providers
- **API Layer** (`NotesApp.Api`): HTTP endpoints, request/response handling, JWT middleware
- **Worker Layer** (`NotesApp.Worker`): Background jobs, outbox message processing, reminder monitoring

### CQRS Implementation
- Use **MediatR** for command and query handling
- **Commands** modify state; **Queries** read state
- Implement **FluentValidation** in the MediatR pipeline
- Use **FluentResults** for consistent error handling across all handlers
- Commands use UnitOfWork to coordinate atomic persistence of domain changes and outbox messages

**Why**: Clean Architecture ensures the business logic is independent from frameworks, databases, and external dependencies. This makes testing easier and allows us to swap implementations without affecting core logic.

---

## 8. Transactional Boundaries & UnitOfWork Pattern

Every **command handler** must establish explicit transactional boundaries. Use **UnitOfWork** to ensure domain changes and outbox messages are coordinated atomically.

### Principles

**Non-Tracking Retrieval Pattern**:
- Retrieve entities untracked by default
- Run all validations, authorization checks, and domain logic
- Only call `Repository.Update()` after all validations pass
- This prevents unintended entity tracking on early returns

**Outbox Message Creation**:
- Always create outbox messages using the generic factory: `OutboxMessage.Create<TAggregate, TEventType>()`
- Use enum-based event types (`NoteEventType.Updated`, `TaskEventType.Created`, etc.) — never magic strings
- Always validate outbox creation result before proceeding to persistence
- Outbox messages encode domain events for reliable background processing

**Atomic Persistence**:
- Call `Repository.Update()` for modified entities
- Call `OutboxRepository.AddAsync()` for outbox messages
- Call `UnitOfWork.SaveChangesAsync()` once — this writes BOTH changes in a single transaction
- Either both succeed or both fail — no orphaned messages or entities

### Key Points

- **Never use `UnitOfWork.Begin()`** — the pattern is: retrieve untracked → validate → attach → save once
- **Always check outbox creation result** — the factory can fail on domain invariants
- **One SaveChangesAsync = one atomic boundary** — all related changes must be in the same call
- **Cache invalidation happens AFTER successful SaveChangesAsync** — never before, to prevent stale cache if persistence fails

**See actual implementation**: 
- `NotesApp.Application/Notes/Commands/UpdateNote/UpdateNoteCommandHandler.cs` — complete pattern with non-tracking retrieval, domain factory validation, outbox creation with error checking, and atomic persistence
- `NotesApp.Application/Notes/Commands/CreateNote/CreateNoteCommandHandler.cs` — variant showing the same pattern for creation
- `NotesApp.Domain/Entities/OutboxMessage.cs` — generic factory definition with invariant validation

**Why**: This pattern ensures reliability (no orphaned messages), consistency (atomic writes), and safety (fail-fast on validation errors). The transactional boundary prevents partial updates and guarantees that background workers always have messages to process for entities that changed.

---

## 9. Outbox Pattern & Reliable Message Processing

The **Outbox Pattern** ensures reliable background processing — messages won't be lost even if the application crashes.

### Pattern Overview

Command handlers create two artifacts in a single atomic transaction:
1. **Domain Entity** (the actual note, task, etc.)
2. **OutboxMessage** (event descriptor for background processing)

Both are persisted together. If the application crashes:
- Both were saved (background worker will process the message)
- Or neither was saved (no inconsistency)

Background worker continuously:
1. Polls for OutboxMessages where `ProcessedAtUtc IS NULL`
2. Deserializes payload and processes based on `MessageType`
3. Updates `ProcessedAtUtc` and `AttemptCount` on success
4. Retries on failure with backoff/circuit breaker (Polly)

### Key Points

- **Message Type Format**: `"AggregateType.EventName"` (e.g., `"Note.Updated"`, `"TaskItem.Created"`)
- **Enum-Based Events**: Use domain enums (`NoteEventType`, `TaskEventType`) not magic strings
- **Payload is JSON**: Includes all context needed by worker (aggregate ID, user ID, changed data, timestamp)
- **At-Least-Once Delivery**: Messages may be processed multiple times; workers must be idempotent

**See actual implementation**: 
- `NotesApp.Domain/Entities/OutboxMessage.cs` — factory and message structure
- `NotesApp.Worker/OutboxProcessingWorker.cs` — background processing loop
- `NotesApp.Application/Notes/Commands/CreateNote/CreateNoteCommandHandler.cs` (lines 78-94) — how outbox payload is serialized
- `NotesApp.Worker/Dispatching/` — message type dispatch logic

**Why**: The outbox pattern is the only reliable way to guarantee background work executes even under failure conditions. It decouples domain operations from background processing and ensures "at-least-once" message delivery.

---

## 10. Caching Strategy & Cache Abstraction

NotesApp uses caching to keep calendar views performant. All cache access **must go through abstraction interfaces**, not concrete implementations.

### Principles

**Cache Abstraction**:
- Always inject `ICalendarCache`, never concrete implementations
- Allows swapping `InMemoryCalendarCache` (development) ↔ `RedisCalendarCache` (production) without handler code changes

**Cache Invalidation**:
- Invalidate cache ONLY after successful persistence (`UnitOfWork.SaveChangesAsync()`)
- If persistence fails, cache remains valid (consistency guaranteed)
- Invalidate at the appropriate granularity (month-level, day-level, etc.)

**Query Pattern**:
- Try cache first
- On cache miss, query database and repopulate cache
- Return result from either cache hit or fresh computation

### Key Points

- **Never invalidate before persistence** — risks stale cache if save fails
- **Use abstraction consistently** — handlers don't care if cache is in-process or distributed
- **Cache keys are user+date specific** — multi-tenancy handled at repository level

**See actual implementation**: 
- `NotesApp.Application/Calendar/Queries/GetMonthOverviewQueryHandler.cs` — cache-first query pattern
- `NotesApp.Application/Notes/Commands/UpdateNote/UpdateNoteCommandHandler.cs` — cache invalidation after successful persist
- `NotesApp.Infrastructure/Persistence/` — ICalendarCache implementations

**Why**: Caching dramatically improves UX for calendar views, but incorrect invalidation causes data inconsistency. Abstraction allows scaling to distributed caching without code changes.

---

## 11. Comprehensive Observability

NotesApp aims for enterprise-grade observability:

- **Structured logging** with Serilog (planned)
- **OpenTelemetry metrics** for performance monitoring (planned)
- **Distributed tracing** to track requests across services
- **Error telemetry** to identify patterns in failures
- Logs include context (user ID, request ID, operation ID) for correlation
- Command handlers log authorization failures, state validation failures, and successful operations

**See actual implementation**: 
- `NotesApp.Application/Notes/Commands/` — logging patterns in handler constructors and Handle methods

**Why**: Production systems need visibility into their behavior. Observability allows us to detect issues before users report them and understand root causes when problems occur.

---

## How to Use This Document

1. **Before starting work**: Read the relevant principles for the task (entity retrieval, transactions, caching, etc.)
2. **When implementing**: Reference the "See actual implementation" sections to examine real code patterns
3. **During code review**: Use these principles as the checklist for PR reviews
4. **When uncertain**: Reference the relevant principle and ask for clarification using your code as the example

## Questions or Clarifications?

If you have questions about any of these principles, or if you discover a principle that needs updating, please raise the discussion with the team. These principles are living guidelines—they should evolve as the project grows.