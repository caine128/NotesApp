using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain
{
    public interface ICalendarEntity 
    {
        Guid UserId { get; }
        DateOnly Date { get; }
    }
}
