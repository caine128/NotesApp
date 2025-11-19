using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Common
{
    /// <summary>
    /// Abstraction over system time to make code testable and avoid DateTime.UtcNow everywhere.
    // TODO : Later, Infrastructure will implement this and return DateTime.UtcNow
    /// </summary>
    public interface ISystemClock
    {
        DateTime UtcNow { get; }
    }
}
