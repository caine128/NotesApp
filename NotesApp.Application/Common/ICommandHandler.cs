using FluentResults;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Common
{
    /// <summary>
    /// Generic command handler abstraction.
    /// Application commands return FluentResults.Result&lt;TResult&gt;.
    /// </summary>
    public interface ICommandHandler<TCommand, TResult>
    {
        Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
    }
}
