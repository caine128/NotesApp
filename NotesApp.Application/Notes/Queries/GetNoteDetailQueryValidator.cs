using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    public sealed class GetNoteDetailQueryValidator : AbstractValidator<GetNoteDetailQuery>
    {
        public GetNoteDetailQueryValidator()
        {
            RuleFor(x => x.NoteId)
                .NotEmpty();
        }
    }
}
