using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Middleware;

/// <summary>
/// Turns any unhandled exception into a generic 500 ProblemDetails response.
/// The full exception is logged server-side; the client only gets a correlation ID to quote,
/// never a message, stack trace or other internal detail.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; there is nobody to respond to and nothing went wrong on our side
            _logger.LogInformation("Request {Method} {Path} was cancelled by the client",
                context.Request.Method, context.Request.Path.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);

            if (context.Response.HasStarted)
            {
                // Status and headers are already sent, so a clean error response is impossible;
                // rethrow and let the server abort the connection
                throw;
            }

            await WriteProblemDetailsAsync(context);
        }
    }

    private static Task WriteProblemDetailsAsync(HttpContext context)
    {
        // Drop anything a handler may have set before failing (partial headers, status code).
        // Callbacks registered with OnStarting survive this, so the correlation ID header is still added.
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "Please try again later. If the problem persists, contact support with the correlation ID.",
            Instance = context.Request.Path,
        };
        problem.Extensions["correlationId"] = context.TraceIdentifier;

        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
