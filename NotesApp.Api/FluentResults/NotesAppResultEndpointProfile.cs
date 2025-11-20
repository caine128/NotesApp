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
    /// 
    /// You can later refine this to map specific error types (NotFound, Forbidden, etc.)
    /// to different HTTP status codes.
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

            var errorMessages = result.Errors
                .Select(e => e.Message)
                .ToArray();

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = "Request validation or business rule failure",
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
