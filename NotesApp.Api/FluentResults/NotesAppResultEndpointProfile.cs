using FluentResults;
using FluentResults.Extensions.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace NotesApp.Api.FluentResults
{
    /// <summary>
    /// Central place that converts FluentResults.Result / Result&lt;T&gt;
    /// into HTTP responses using RFC7807 ProblemDetails.
    ///
    /// - Successful results: default behaviour (200 + value, 204 for no-value).
    /// - Failed results: we build a ProblemDetails payload with an "errors" array.
    /// - Specific ErrorCodes can be mapped to specific HTTP status codes.
    /// </summary>
    public sealed class NotesAppResultEndpointProfile : DefaultAspNetCoreResultEndpointProfile
    {
        /// <summary>
        /// Called whenever a failed Result should be transformed to an ActionResult.
        /// </summary>
        public override ActionResult TransformFailedResultToActionResult(
            FailedResultToActionResultTransformationContext context)
        {
            var result = context.Result;

            // For now we treat all domain/application failures as "bad request".
            // TODO : Later you can inspect result.Errors and return 404, 401, 409, etc.
            int statusCode = StatusCodes.Status400BadRequest;

            // Extract any ErrorCode metadata values from the errors.
            var errorCodes = result.Errors
                .Select(e =>
                    e.Metadata.TryGetValue("ErrorCode", out var value) && value is string s
                        ? s
                        : null)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToList();

            // Map known error codes to specific HTTP status codes.
            if (errorCodes.Contains("Tasks.NotFound") ||
            errorCodes.Contains("Notes.NotFound"))
            {
                statusCode = StatusCodes.Status404NotFound;
            }

            var errorMessages = result.Errors
                .Select(e => e.Message)
                .ToArray();

            var title = statusCode switch
            {
                StatusCodes.Status404NotFound => "Resource not found",
                _ => "Request validation or business rule failure"
            };

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = errorMessages.Length == 1
                    ? errorMessages[0]
                    : "Multiple errors occurred. See 'errors' for details."
            };

            // Expose structured error info for the client (e.g. UI can show a list)
            problem.Extensions["errors"] = result.Errors
                .Select(e => new
                {
                    e.Message,
                    e.Metadata   // FluentResults lets us attach metadata
                })
                .ToArray();

            return new ObjectResult(problem)
            {
                StatusCode = statusCode
            };
        }

        // Successful results (Result and Result<T>) use the base implementation:
        // - Result      -> 204 No Content
        // - Result<T>   -> 200 OK + T
    }
}
