using NotesApp.Application.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Time
{
    public sealed class SystemClock : ISystemClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
