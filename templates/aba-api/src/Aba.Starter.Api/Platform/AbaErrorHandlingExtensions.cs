using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aba.Starter.Api.Platform;

/// <summary>
/// Domain-neutral HTTP error and correlation handling shared by the production
/// host and its deterministic HTTP tests. No test-only route is exposed.
/// </summary>
public static class AbaErrorHandlingExtensions
{
    private static readonly object CorrelationIdKey = new();

    public static IServiceCollection AddAbaErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                // Use the same validated correlation ID for the problem body
                // and response header, even when exception handling clears headers.
                var correlationId = context.HttpContext.Items.TryGetValue(
                    CorrelationIdKey, out var value) && value is string id
                    ? id
                    : context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
            };
        });
        return services;
    }

    public static IApplicationBuilder UseAbaErrorHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            var requestedId = context.Request.Headers["X-Correlation-ID"].ToString();
            var valid = requestedId.Length is > 0 and <= 64
                && requestedId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
            var correlationId = valid ? requestedId : context.TraceIdentifier;
            context.Items[CorrelationIdKey] = correlationId;

            // ExceptionHandlerMiddleware may clear headers when replacing an
            // exception with a 500 response. Assign the header when sending
            // the response so normal and failure paths keep the same ID.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Correlation-ID"] = correlationId;
                return Task.CompletedTask;
            });
            await next();
        });
        return app;
    }
}
