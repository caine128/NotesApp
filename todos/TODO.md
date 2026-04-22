# NotesApp — Recurring Tasks Feature: Outstanding TODOs

## Pending Migrations

- [ ] **`AddIsCompletedToRecurringTaskException`**
  Adds `IsCompleted bit NOT NULL DEFAULT 0` to the `RecurringTaskExceptions` table.
  Run manually:
  ```
  dotnet ef migrations add AddIsCompletedToRecurringTaskException --project NotesApp.Infrastructure --startup-project NotesApp.Api
  ```

---

## Known Gaps / Follow-up Work

- [ ] **RRuleString parse-attempt validation**
  The `CreateTaskCommandValidator` only checks that `RRuleString` contains `"FREQ="`. It does not attempt
  to parse the string with Ical.Net. A malformed but `FREQ=`-containing string (e.g. `"FREQ=BLAH"`)
  will pass validation and then throw an unhandled exception inside `RecurrenceEngine.GenerateOccurrences`,
  returning a 500 instead of a 400.

  Fix: add a parse-attempt validator rule. Because `Ical.Net` must not be referenced from
  `NotesApp.Application`, options are:
  - Expose a `bool TryParse(string rruleString)` method on `IRecurrenceEngine` and call it from a
    custom FluentValidation validator that depends on the interface.
  - Or validate in a domain service called from the handler before `RecurringTaskSeries.Create`.

---

## Completed (this session)

- [x] Atomicity audit — `SubtaskRepository.SoftDeleteAllForTaskAsync` and
      `AttachmentRepository.SoftDeleteAllForTaskAsync` converted from `ExecuteUpdateAsync`
      to change-tracker pattern (fully atomic with caller's `SaveChangesAsync`).
- [x] `RecurringTaskException.IsCompleted` — added field, EF config, `CreateOverride` /
      `UpdateOverride` signatures, propagated through command handler, virtual occurrence
      projection (`TaskRepository`), query handler, sync pull DTO, sync push DTOs,
      `SyncPushCommandHandler` call sites, and `SyncMappings.ToSyncDto`.
- [x] `RecurringReminderHelper` — made `public` so `TaskRepository` (Infrastructure) can call it;
      virtual occurrence `ReminderAtUtc` now correctly computed in `ProjectVirtualOccurrence`.
- [x] `TaskOccurrenceResult` — `IsCompleted` and `ReminderAtUtc` moved out of
      "materialized-only" section; doc comments updated to describe both sources.
- [x] `UpdateRecurringTaskOccurrenceCommandHandler` — `SetCompleted` replaced with correct
      `MarkCompleted` / `MarkPending` calls.
- [x] `RecurrenceRuleDto` moved to `Tasks/Models/RecurrenceRuleDto.cs`.
- [x] `TemplateSubtaskDto` moved to `Subtasks/Models/TemplateSubtaskDto.cs`;
      `TemplateSubtaskUpdateDto` (identical shape) consolidated into `TemplateSubtaskDto`.
- [x] `RecurringTaskMaterializerService` — all silent `continue` guards replaced with
      `Result.Fail<MaterializationBatch>` propagation; interface return types changed from
      `MaterializationBatch` to `Result<MaterializationBatch>` (FluentResults); both callers
      (`CreateTaskCommandHandler`, `RecurringTaskHorizonWorker`) updated to handle the result.
- [x] `RecurrenceEngine` — `EvaluationOptions.MaxUnmatchedIncrementsLimit = 1000` added as
      safety guard; manual `yield break` loop replaced with `TakeWhileBefore(periodEnd)` (Ical.Net 5.1+
      idiomatic pattern); `effectiveEnd` collapses `toExclusive` and `endsBeforeDate` into one bound.
