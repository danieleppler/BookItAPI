using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BookIt.Api.Middleware;

/// <summary>
/// Assigns a correlation ID to every request, returns it in the response headers, and logs
/// method, path, status code and duration once the request completes.
/// Registered first in the pipeline, so it also times and logs requests that fail further in.
/// </summary>
public partial class RequestLoggingMiddleware
{
    public const string CorrelationIdHeader = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        // TraceIdentifier is the framework's per-request id; other components (e.g. the exception handler) read it from here
        context.TraceIdentifier = correlationId;

        // Headers can only be set before the response starts. OnStarting runs right before the first byte is sent,
        // so the header is added even if a later middleware clears the response (as the exception handler does).
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        var start = Stopwatch.GetTimestamp();

        // Every log written during this request (controllers, services, EF) carries the correlation ID in its scope
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        var failed = false;
        try
        {
            await _next(context);
        }
        catch
        {
            // Normally the exception middleware handles errors before they get here; this is a safety net
            failed = true;
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var statusCode = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;

            // Path only, not the query string: query values can carry user data that shouldn't end up in logs
            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms [CorrelationId: {CorrelationId}]",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                elapsedMs,
                correlationId);
        }
    }

    /// <summary>
    /// Reuses the caller's correlation ID so a request can be traced across services, but only if it's a short,
    /// safe token; anything else (too long, odd characters that could forge log lines) is replaced with a new id.
    /// </summary>
    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        var incoming = context.Request.Headers[CorrelationIdHeader].ToString();

        return !string.IsNullOrEmpty(incoming) && SafeCorrelationId().IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeCorrelationId();
}
