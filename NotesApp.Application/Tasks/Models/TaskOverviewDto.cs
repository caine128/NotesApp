using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Models
{
    public sealed record TaskOverviewDto(string Title,
                                         DateOnly Date);
}
