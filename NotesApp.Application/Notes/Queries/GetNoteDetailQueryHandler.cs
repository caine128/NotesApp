using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    public sealed class GetNoteDetailQueryHandler
    : IRequestHandler<GetNoteDetailQuery, Result<NoteDetailDto>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetNoteDetailQueryHandler(INoteRepository noteRepository,
                                         ICurrentUserService currentUserService)
        {
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<NoteDetailDto>> Handle(GetNoteDetailQuery request,
                                                        CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var note = await _noteRepository.GetByIdAsync(request.NoteId, cancellationToken);

            if (note is null || note.UserId != userId)
            {
                return Result.Fail(
                    new Error("Note.NotFound")
                          .WithMetadata("ErrorCode", "Notes.NotFound"));
            }

            var dto = note.ToDetailDto();
            return Result.Ok(dto);
        }
    }
}
