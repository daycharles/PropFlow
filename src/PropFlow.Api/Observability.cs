using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PropFlow.Api;

public static class PropFlowObservability
{
    public const string ActivitySourceName = "PropFlow.Api";
    public const string MeterName = "PropFlow.Api";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "propflow.http.server.requests", unit: "{request}", description: "HTTP requests handled by the API.");
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "propflow.http.server.duration", unit: "ms", description: "HTTP request duration.");
    private static long totalRequests;
    private static long failedRequests;
    private static long totalDurationMilliseconds;

    public static void RecordRequest(int statusCode, double durationMilliseconds)
    {
        Interlocked.Increment(ref totalRequests);
        if (statusCode >= 500) Interlocked.Increment(ref failedRequests);
        Interlocked.Add(ref totalDurationMilliseconds, (long)Math.Round(durationMilliseconds));
    }

    public static object Snapshot() => new
    {
        requests = Volatile.Read(ref totalRequests),
        failures = Volatile.Read(ref failedRequests),
        averageDurationMs = totalRequests == 0 ? 0 : (double)Volatile.Read(ref totalDurationMilliseconds) / totalRequests
    };
}

public sealed class RequestObservabilityMiddleware(RequestDelegate next, ILogger<RequestObservabilityMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.Headers["X-Trace-ID"] = traceId;
        var started = Stopwatch.GetTimestamp();

        using (logger.BeginScope(new Dictionary<string, object?> { ["trace_id"] = traceId }))
        {
            try
            {
                await next(context);
            }
            finally
            {
                var duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var tags = new TagList
                {
                    { "http.request.method", context.Request.Method },
                    { "http.response.status_code", context.Response.StatusCode },
                    { "http.route", context.GetEndpoint()?.DisplayName ?? "unmatched" }
                };
                PropFlowObservability.Requests.Add(1, tags);
                PropFlowObservability.RequestDuration.Record(duration, tags);
                PropFlowObservability.RecordRequest(context.Response.StatusCode, duration);
                logger.LogInformation("HTTP request completed with status {StatusCode} in {DurationMs:0.0} ms", context.Response.StatusCode, duration);
            }
        }
    }
}
