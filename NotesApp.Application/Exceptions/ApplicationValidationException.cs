using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Exceptions
{
    public sealed class ApplicationValidationException : Exception
    {
        public IReadOnlyDictionary<string, string[]> Errors { get; }

        public ApplicationValidationException(IReadOnlyDictionary<string, string[]> errors)
                                        : base("One or more validation failures have occurred.")
        {
            Errors = errors;
        }
    }
}
