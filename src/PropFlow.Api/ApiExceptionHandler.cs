using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;

namespace PropFlow.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            TenantAccessException => (403, "Organization access denied"),
            DbUpdateConcurrencyException => (409, "Work changed; reload and try again"),
            BadHttpRequestException => (400, "Invalid request"),
            _ => (500, "An unexpected error occurred")
        };
        if (status == 500) logger.LogError(exception, "Request failed. TraceId: {TraceId}", context.TraceIdentifier);
        await Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }
}
