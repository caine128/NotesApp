using FluentValidation;
using MediatR;
using NotesApp.Application.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Common.Behaviors
{
    /// <summary>
    /// Runs FluentValidation validators for a request before its handler executes.
    /// If there are validation failures, throws a ValidationException.
    /// This is the standard MediatR + FluentValidation pattern.
    /// </summary>
    public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                                                                    where TRequest : notnull, IRequest<TResponse>
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task<TResponse> Handle(TRequest request,
                                            RequestHandlerDelegate<TResponse> next,
                                            CancellationToken cancellationToken)
        {
            if (!_validators.Any())
            {
                // no validators registered for this request -> continue
                return await next(cancellationToken);
            }

            var context = new ValidationContext<TRequest>(request);

            var validationResults = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = validationResults.SelectMany(r => r.Errors)
                                             .Where(f => f is not null)
                                             .ToList();

            if (failures.Count > 0)
            {
                // Group by property name: "Title" -> ["Title is required", "Title too long"]
                var errorDictionary = failures
                    .GroupBy(f => f.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(f => f.ErrorMessage).ToArray());

                // Throw our own exception, not FluentValidation's
                throw new ApplicationValidationException(errorDictionary);
            }

            return await next(cancellationToken);
        }
    }
}
