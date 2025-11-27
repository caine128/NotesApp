using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetTaskOverviewForRangeQueryValidator
    : AbstractValidator<GetTaskOverviewForRangeQuery>
    {
        public GetTaskOverviewForRangeQueryValidator()
        {
            RuleFor(x => x.Start)
                .NotEqual(default(DateOnly));

            RuleFor(x => x.EndExclusive)
                .NotEqual(default(DateOnly));

            RuleFor(x => x)
                .Must(x => x.EndExclusive > x.Start)
                .WithMessage("EndExclusive must be greater than Start.");
        }
    }
}
