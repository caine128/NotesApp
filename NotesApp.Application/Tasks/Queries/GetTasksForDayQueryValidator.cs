using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetTasksForDayQueryValidator
                : AbstractValidator<GetTasksForDayQuery>
    {
        public GetTasksForDayQueryValidator()
        {
            RuleFor(x => x.Date)
                .Must(d => d != default)
                .WithMessage("Date is required.");
        }
    }
}
