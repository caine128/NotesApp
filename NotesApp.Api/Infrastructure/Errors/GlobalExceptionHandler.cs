using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Exceptions;

namespace NotesApp.Api.Infrastructure.Errors
{
    /// <summary>
    /// Central place where all unhandled exceptions are turned into
    /// RFC-compliant ProblemDetails responses.
    /// Uses IProblemDetailsService behind the scenes.
    /// </summary>
    public sealed class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetailsService;
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(IProblemDetailsService problemDetailsService,
                                      ILogger<GlobalExceptionHandler> logger)
        {
            _problemDetailsService = problemDetailsService;
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
                                                    Exception exception,
                                                    CancellationToken cancellationToken)
        {
            // 1. Log every unhandled exception
            _logger.LogError(exception, "Unhandled exception caught by GlobalExceptionHandler");

            var statusCode = exception switch
            {
                ApplicationValidationException => StatusCodes.Status400BadRequest,
                // TODO :  NotFoundException => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            };

            var title = statusCode switch
            {
                StatusCodes.Status400BadRequest => "Validation error",
                StatusCodes.Status404NotFound => "Resource not found",
                StatusCodes.Status500InternalServerError => "An error occurred while processing your request.",
                _ => "An error occurred."
            };

            // Basic ProblemDetails; AddProblemDetails() will fill in type/status/etc.
            var problemDetails = new ProblemDetails
            {
                Title = title,
                Detail = exception.Message, // consider hiding details in production
                Status = statusCode,
                Instance = httpContext.Request.Path
            };

            // If this is an application validation exception, include per-field errors
            if (exception is ApplicationValidationException validationException)
            {   
                problemDetails.Extensions["errors"] = validationException.Errors;
            }

            httpContext.Response.StatusCode = statusCode;

            var writeResult = await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
                Exception = exception
            });

            // Returning true = "we handled it, stop the pipeline here"
            return writeResult;
        }
    }
}
