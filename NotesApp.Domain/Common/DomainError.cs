using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Represents a domain-level validation or business rule error.
    /// </summary>
    public sealed class DomainError
    {
        public string Code { get; }
        public string Message { get; }

        public DomainError(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString() => $"{Code}: {Message}";
    }
}
