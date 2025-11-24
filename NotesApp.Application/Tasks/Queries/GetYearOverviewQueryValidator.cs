using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetYearOverviewQueryValidator : AbstractValidator<GetYearOverviewQuery>
    {
        public GetYearOverviewQueryValidator()
        {
            RuleFor(x => x.Year)
                .InclusiveBetween(2000, 2100)
                .WithMessage("Year must be between 2000 and 2100.");
        }
    }
}
