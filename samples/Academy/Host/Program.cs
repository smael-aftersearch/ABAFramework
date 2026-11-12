using Academy.Host;
using Aba.Caching;
using Aba.Foundation;
using Aba.LocalMessaging;
using Academy.Accounting.Application;
using Academy.Accounting.Infrastructure;
using Academy.Enrollment.Application;
using Academy.Enrollment.Contracts;
using Academy.Enrollment.Infrastructure;
using Academy.Identity.Application;
using Academy.Identity.Contracts;
using Academy.Identity.Infrastructure;
using Academy.Payments.Application;
using Academy.Payments.Infrastructure;
using Academy.Reporting.Application;
using Academy.Reporting.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// This iteration has NO OAuth/OIDC protection. Refuse non-development execution.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "Phase 1 is a LOCAL DEVELOPMENT DEMO only. Authentication and production storage are not implemented.");

var dataDir = builder.Configuration["ACADEMY_DATA_DIR"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "local-data");
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IReadCache, MemoryReadCache>();
builder.Services.AddSingleton(sp => new DurableContextStore<IdentityData>(
    "Identity", Path.Combine(dataDir, "identity.json"), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton(sp => new DurableContextStore<EnrollmentData>(
    "Enrollment", Path.Combine(dataDir, "enrollment.json"), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton(sp => new DurableContextStore<PaymentsData>(
    "Payments", Path.Combine(dataDir, "payments.json"), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton(sp => new DurableContextStore<AccountingData>(
    "Accounting", Path.Combine(dataDir, "accounting.json"), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton(sp => new DurableContextStore<ReportingData>(
    "Reporting", Path.Combine(dataDir, "reporting.json"), sp.GetRequiredService<IClock>()));

builder.Services.AddSingleton<IdentityService>();
builder.Services.AddSingleton<IIdentityReaderV1>(sp => sp.GetRequiredService<IdentityService>());
builder.Services.AddSingleton<EnrollmentService>();
builder.Services.AddSingleton<IEnrollmentPaymentQuoteV1>(sp => sp.GetRequiredService<EnrollmentService>());
builder.Services.AddSingleton<FakePaymentProvider>();
builder.Services.AddSingleton<PaymentsService>();
builder.Services.AddSingleton<AccountingService>();
builder.Services.AddSingleton<ReportingService>();

builder.Services.AddSingleton<IOutboxSource>(sp => sp.GetRequiredService<DurableContextStore<IdentityData>>());
builder.Services.AddSingleton<IOutboxSource>(sp => sp.GetRequiredService<DurableContextStore<EnrollmentData>>());
builder.Services.AddSingleton<IOutboxSource>(sp => sp.GetRequiredService<DurableContextStore<PaymentsData>>());
builder.Services.AddSingleton<IOutboxSource>(sp => sp.GetRequiredService<DurableContextStore<AccountingData>>());
builder.Services.AddSingleton<IOutboxSource>(sp => sp.GetRequiredService<DurableContextStore<ReportingData>>());
builder.Services.AddSingleton<IIntegrationEventHandler, PaymentCapturedEnrollmentHandler>();
builder.Services.AddSingleton<IIntegrationEventHandler, PaymentToJournalHandler>();
builder.Services.AddSingleton<IIntegrationEventHandler, ReportingProjectionHandler>();
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxWorker>();

var app = builder.Build();
app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var known = exception is DomainRuleException;
    context.Response.StatusCode = known ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = context.Response.StatusCode,
        Title = known ? "Business rule rejected" : "Unexpected server error",
        Detail = known ? exception!.Message : "An unexpected error occurred."
    });
}));

app.MapGet("/health/live", () => Results.Ok(new { status = "live", stage = "phase1-local-demo" }));
var api = app.MapGroup("/api/v1");
api.MapGet("/catalog/courses", (EnrollmentService enrollment, CancellationToken ct)
    => enrollment.CoursesAsync(ct));
api.MapPost("/users", async (RegisterUserRequest request, IdentityService identity, CancellationToken ct) =>
{
    var user = await identity.RegisterAsync(request.Email, ct);
    return Results.Created($"/api/v1/users/{user.UserId}", user);
});
api.MapGet("/users/{id:guid}", async (Guid id, IdentityService identity, CancellationToken ct) =>
{
    var result = await identity.GetAsync(id, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});
api.MapPost("/enrollments", async (CreateEnrollmentRequest request, EnrollmentService enrollment, CancellationToken ct) =>
{
    var created = await enrollment.RequestAsync(request.UserId, request.CourseId, ct);
    return Results.Created($"/api/v1/enrollments/{created.EnrollmentId}", created);
});
api.MapGet("/enrollments/{id:guid}", async (Guid id, EnrollmentService enrollment, CancellationToken ct) =>
{
    var result = await enrollment.GetAsync(id, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});
api.MapPost("/payments/capture", async (CapturePaymentRequest request, HttpRequest http,
    PaymentsService payments, CancellationToken ct) =>
{
    var key = http.Headers["Idempotency-Key"].ToString();
    return Results.Ok(await payments.CaptureAsync(request.EnrollmentId, key, ct));
});
api.MapGet("/accounting/journals", (AccountingService accounting, CancellationToken ct)
    => accounting.ListJournalsAsync(ct));
api.MapGet("/reporting/summary", (ReportingService reporting, CancellationToken ct)
    => reporting.SummaryAsync(ct));

// Development only: hand-authored OpenAPI starter; until generators are integrated,
// keep changes to this file in lockstep with endpoints.
app.MapGet("/openapi/v1.json", async (CancellationToken ct) =>
    Results.File(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "openapi-v1.json"), ct),
        "application/json"));

app.Run();

public sealed record RegisterUserRequest(string Email);
public sealed record CreateEnrollmentRequest(Guid UserId, Guid CourseId);
public sealed record CapturePaymentRequest(Guid EnrollmentId);
