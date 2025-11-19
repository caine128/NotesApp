using FluentResults;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Common
{
    public static class DomainResultExtensions
    {
        /// <summary>
        /// Maps a DomainResult (no value) to a FluentResults.Result.
        /// </summary>
        public static Result ToResult(this DomainResult domainResult)
        {
            if (domainResult.IsSuccess)
                return Result.Ok();

            var errors = domainResult.Errors
                .Select(e => new Error(e.Message).WithMetadata("Code", e.Code));

            return Result.Fail(errors);
        }

        /// <summary>
        /// Maps a DomainResult<TSource> to Result<TDest> using a mapper function for the value.
        /// If the domain result failed, copies its errors to the Result.
        /// </summary>
        public static Result<TDest> ToResult<TSource, TDest>(this DomainResult<TSource> domainResult,
                                                             Func<TSource, TDest> mapper)
        {
            if (domainResult.IsSuccess && domainResult.Value is not null)
            {
                var mapped = mapper(domainResult.Value);
                return Result.Ok(mapped);
            }

            var errors = domainResult.Errors
                                 .Select(e => new Error(e.Code)     // code as main text/id
                                 .WithMetadata("Message", e.Message)); // human-readable message

            return Result.Fail<TDest>(errors);
        }

        /// <summary>
        /// Maps a DomainResult (no value) to Result<TDest> using a value factory.
        /// Useful when domain logic succeeded but there is no DomainResult&lt;T&gt;.
        /// </summary>
        public static Result<TDest> ToResult<TDest>(this DomainResult domainResult,
                                                    Func<TDest> valueFactory)
        {
            if (domainResult.IsSuccess)
            {
                var value = valueFactory();
                return Result.Ok(value);
            }

            var errors = domainResult.Errors
                                 .Select(e => new Error(e.Code)
                                 .WithMetadata("Message", e.Message));

            return Result.Fail<TDest>(errors);
        }
    }
}
