using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using PropFlow.Application;

namespace PropFlow.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var (status, title) = exception switch
        {
            TenantAccessException => (403, "Organization access denied"),
            DbUpdateConcurrencyException => (409, "Work changed; reload and try again"),
            BadHttpRequestException => (400, "Invalid request"),
            // Domain guard violations (ArgumentException and its ArgumentNullException /
            // ArgumentOutOfRangeException subtypes) are caller input errors, not server faults.
            ArgumentException => (400, "Invalid request"),
            _ => (500, "An unexpected error occurred")
        };
        if (status == 500) logger.LogError(exception, "Request failed. TraceId: {TraceId}", traceId);
        await Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["traceId"] = traceId }).ExecuteAsync(context);
        return true;
    }
}
