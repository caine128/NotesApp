using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetTaskDetailQueryValidator : AbstractValidator<GetTaskDetailQuery>
    {
        public GetTaskDetailQueryValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty();
        }
    }
}
