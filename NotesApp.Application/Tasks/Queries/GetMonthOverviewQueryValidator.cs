using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetMonthOverviewQueryValidator : AbstractValidator<GetMonthOverviewQuery>
    {
        public GetMonthOverviewQueryValidator()
        {
            RuleFor(x => x.Year)
                .InclusiveBetween(2000, 2100)
                .WithMessage("Year must be between 2000 and 2100.");

            RuleFor(x => x.Month)
                .InclusiveBetween(1, 12)
                .WithMessage("Month must be between 1 and 12.");
        }
    }
}
