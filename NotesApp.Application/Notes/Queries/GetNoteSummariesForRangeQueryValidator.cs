using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    public sealed class GetNoteSummariesForRangeQueryValidator
    : AbstractValidator<GetNoteSummariesForRangeQuery>
    {
        public GetNoteSummariesForRangeQueryValidator()
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
