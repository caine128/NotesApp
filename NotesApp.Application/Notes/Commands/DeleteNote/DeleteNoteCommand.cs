using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.DeleteNote
{
    public sealed record DeleteNoteCommand(Guid NoteId)
    : IRequest<Result>;
}
