using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Non-generic domain result, used when there's no value to return.
    /// </summary>
    public class DomainResult
    {
        private static readonly DomainResult _success = new(true, Array.Empty<DomainError>());

        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;

        public IReadOnlyList<DomainError> Errors { get; }

        protected DomainResult(bool isSuccess, IReadOnlyList<DomainError> errors)
        {
            IsSuccess = isSuccess;
            Errors = errors;
        }

        public static DomainResult Success() => _success;

        public static DomainResult Failure(params DomainError[] errors)
            => new(false, errors);

        public static DomainResult Failure(IEnumerable<DomainError> errors)
            => new(false, errors.ToArray());
    }

    /// <summary>
    /// Domain result that carries a value on success.
    /// </summary>
    public sealed class DomainResult<T> : DomainResult
    {
        public T? Value { get; }

        private DomainResult(bool isSuccess, T? value, IReadOnlyList<DomainError> errors)
            : base(isSuccess, errors)
        {
            Value = value;
        }

        public static DomainResult<T> Success(T value)
            => new(true, value, Array.Empty<DomainError>());

        public new static DomainResult<T> Failure(params DomainError[] errors)
            => new(false, default, errors);

        public new static DomainResult<T> Failure(IEnumerable<DomainError> errors)
            => new(false, default, errors.ToArray());
    }
}
