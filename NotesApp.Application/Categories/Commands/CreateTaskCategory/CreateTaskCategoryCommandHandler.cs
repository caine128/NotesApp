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

namespace NotesApp.Application.Categories.Commands.CreateTaskCategory
{
    /// <summary>
    /// Handles the CreateTaskCategoryCommand:
    /// - Resolves the current internal user id from the token.
    /// - Creates the TaskCategory domain entity with validation.
    /// - Creates an outbox message BEFORE persisting (atomic reliability).
    /// - Persists category and outbox message atomically via IUnitOfWork.
    ///
    /// Returns:
    /// - Result.Ok(TaskCategoryDto) -> HTTP 201 Created
    /// - Failure results            -> HTTP 400 via global mapping
    /// </summary>
    public sealed class CreateTaskCategoryCommandHandler
        : IRequestHandler<CreateTaskCategoryCommand, Result<TaskCategoryDto>>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<CreateTaskCategoryCommandHandler> _logger;

        public CreateTaskCategoryCommandHandler(
            ICategoryRepository categoryRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<CreateTaskCategoryCommandHandler> logger)
        {
            _categoryRepository = categoryRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<TaskCategoryDto>> Handle(
            CreateTaskCategoryCommand command,
            CancellationToken cancellationToken)
        {
            // 1) Resolve current internal user Id from token/claims.
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // 2) Create the domain entity with invariant validation.
            var createResult = TaskCategory.Create(userId, command.Name, utcNow);

            if (createResult.IsFailure)
            {
                return createResult.ToResult<TaskCategory, TaskCategoryDto>(c => c.ToDto());
            }

            var category = createResult.Value!;

            // 3) Create outbox message BEFORE touching persistence.
            var payload = JsonSerializer.Serialize(new
            {
                CategoryId = category.Id,
                category.UserId,
                category.Name,
                category.Version,
                Event = TaskCategoryEventType.Created.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<TaskCategory, TaskCategoryEventType>(
                aggregate: category,
                eventType: TaskCategoryEventType.Created,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult<OutboxMessage, TaskCategoryDto>(_ => category.ToDto());
            }

            // 4) Persist category and outbox atomically.
            await _categoryRepository.AddAsync(category, cancellationToken);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Category {CategoryId} '{Name}' created for user {UserId}.",
                                   category.Id, category.Name, userId);

            return Result.Ok(category.ToDto());
        }
    }
}
