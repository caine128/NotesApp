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
            RuleFor(x => x.UserId)
                .NotEmpty()
                .WithMessage("User id must be specified.");

            // Date is a value type (DateOnly), so it’s always set.
            // If later you have rules like “not in the past”, you can add them here.
        }
    }
}
