using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Categories.Models;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System.Text.Json;

namespace NotesApp.Application.Categories.Commands.UpdateTaskCategory
{
    /// <summary>
    /// Handles the UpdateTaskCategoryCommand:
    /// - Loads the category WITHOUT tracking (non-tracking by default — CODING_PRINCIPLES #2).
    /// - Validates ownership; returns NotFound for both null and wrong-user to prevent
    ///   information leakage.
    /// - Applies the rename through the TaskCategory domain method.
    /// - Creates an outbox message BEFORE persisting.
    /// - Persists atomically via IUnitOfWork.
    ///
    /// Returns:
    /// - Result.Ok(TaskCategoryDto)          -> HTTP 200 OK
    /// - Result.Fail (Categories.NotFound)   -> HTTP 404 Not Found
    /// - Other failures                       -> HTTP 400 via global mapping
    /// </summary>
    public sealed class UpdateTaskCategoryCommandHandler
        : IRequestHandler<UpdateTaskCategoryCommand, Result<TaskCategoryDto>>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<UpdateTaskCategoryCommandHandler> _logger;

        public UpdateTaskCategoryCommandHandler(
            ICategoryRepository categoryRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<UpdateTaskCategoryCommandHandler> logger)
        {
            _categoryRepository = categoryRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<TaskCategoryDto>> Handle(
            UpdateTaskCategoryCommand command,
            CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id.
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load WITHOUT tracking — modifications won't auto-persist on early return.
            var category = await _categoryRepository.GetByIdUntrackedAsync(
                command.CategoryId, cancellationToken);

            if (category is null || category.UserId != currentUserId)
            {
                _logger.LogWarning(
                    "UpdateTaskCategory failed: category {CategoryId} not found for user {UserId}.",
                    command.CategoryId, currentUserId);

                return Result.Fail<TaskCategoryDto>(
                    new Error("Category not found.")
                        .WithMetadata("ErrorCode", "Categories.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Domain rename — entity is NOT tracked, so modifications are in-memory only.
            var updateResult = category.Update(command.Name, utcNow);

            if (updateResult.IsFailure)
            {
                return updateResult.ToResult(() => category.ToDto());
            }

            // 4) Create outbox message BEFORE persisting.
            var payload = JsonSerializer.Serialize(new
            {
                CategoryId = category.Id,
                category.UserId,
                category.Name,
                category.Version,
                Event = TaskCategoryEventType.Updated.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<TaskCategory, TaskCategoryEventType>(
                aggregate: category,
                eventType: TaskCategoryEventType.Updated,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult<OutboxMessage, TaskCategoryDto>(_ => category.ToDto());
            }

            // 5) SUCCESS: explicitly attach (was loaded untracked) and persist atomically.
            _categoryRepository.Update(category);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Category {CategoryId} renamed to '{Name}' for user {UserId}.",
                category.Id, category.Name, currentUserId);

            return Result.Ok(category.ToDto());
        }
    }
}
