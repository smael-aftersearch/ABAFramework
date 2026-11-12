using Aba.Caching;
using Aba.Foundation;
using Aba.LocalMessaging;
using Academy.Accounting.Application;
using Academy.Accounting.Domain;
using Academy.Accounting.Infrastructure;
using Academy.Enrollment.Application;
using Academy.Enrollment.Domain;
using Academy.Enrollment.Infrastructure;
using Academy.Identity.Application;
using Academy.Identity.Infrastructure;
using Academy.Payments.Application;
using Academy.Payments.Contracts;
using Academy.Payments.Infrastructure;
using Academy.Reporting.Application;
using Academy.Reporting.Infrastructure;

var dir = Path.Combine(Path.GetTempPath(), "aba-phase1-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var clock = new SystemClock();
    var identityStore = new DurableContextStore<IdentityData>("Identity", Path.Combine(dir, "identity.json"), clock);
    var enrollmentStore = new DurableContextStore<EnrollmentData>("Enrollment", Path.Combine(dir, "enrollment.json"), clock);
    var paymentsStore = new DurableContextStore<PaymentsData>("Payments", Path.Combine(dir, "payments.json"), clock);
    var accountingStore = new DurableContextStore<AccountingData>("Accounting", Path.Combine(dir, "accounting.json"), clock);
    var reportingStore = new DurableContextStore<ReportingData>("Reporting", Path.Combine(dir, "reporting.json"), clock);
    var cache = new MemoryReadCache();
    var identity = new IdentityService(identityStore, clock);
    var enrollment = new EnrollmentService(enrollmentStore, identity, clock);
    var payments = new PaymentsService(paymentsStore, enrollment, new FakePaymentProvider(), clock);
    var accounting = new AccountingService(accountingStore);
    var reporting = new ReportingService(reportingStore, cache);
    var paymentToEnrollment = new PaymentCapturedEnrollmentHandler(enrollmentStore, clock);
    var paymentToAccounting = new PaymentToJournalHandler(accountingStore, clock);
    var reportingHandler = new ReportingProjectionHandler(reportingStore, cache);
    var dispatcher = new OutboxDispatcher(
        [identityStore, enrollmentStore, paymentsStore, accountingStore, reportingStore],
        [paymentToEnrollment, paymentToAccounting, reportingHandler]);

    var initial = await reporting.SummaryAsync();
    Check(initial.UsersRegistered == 0, "Initial projection empty");

    var alice = await identity.RegisterAsync(" Alice@Example.org ");
    Check(alice.Email == "alice@example.org", "Identity normalizes address");
    await DrainAsync(dispatcher);
    Check((await reporting.SummaryAsync()).UsersRegistered == 1, "Projection cache invalidated on event");

    var courses = await enrollment.CoursesAsync();
    Check(courses.Count == 1 && courses[0].CourseId == Course.DemoCourseId, "Seeded bounded-context catalog");
    var first = await enrollment.RequestAsync(alice.UserId, Course.DemoCourseId);
    Check(first.Status == "PendingPayment", "Enrollment starts pending payment");
    var capture = await payments.CaptureAsync(first.EnrollmentId, "payment-for-alice");
    var eventForReplay = (await paymentsStore.PeekDueAsync(10)).Single();
    Check(eventForReplay.EventType == EventType.Of<PaymentCapturedV1>(), "Versioned payment contract in outbox");
    var repeatBeforeDelivery = await payments.CaptureAsync(first.EnrollmentId, "payment-for-alice");
    Check(repeatBeforeDelivery.PaymentId == capture.PaymentId, "Payment request idempotency before delivery");
    await DrainAsync(dispatcher);
    Check((await enrollment.GetAsync(first.EnrollmentId))!.Status == "Enrolled", "Enrollment owns activation after payment");
    Check((await accounting.ListJournalsAsync()).Count == 1, "Accounting owns posting");
    Check((await accounting.ListJournalsAsync()).All(j => j.IsBalanced), "Accounting journal balance invariant");
    var afterFirst = await reporting.SummaryAsync();
    Check(afterFirst.PaymentsCaptured == 1 && afterFirst.EnrollmentsActivated == 1 &&
          afterFirst.JournalsPosted == 1 && afterFirst.RevenueCapturedMinor == 1999,
        "Cross-context reporting projection updates via contracts");

    await paymentToAccounting.HandleAsync(eventForReplay);
    await paymentToEnrollment.HandleAsync(eventForReplay);
    await reportingHandler.HandleAsync(eventForReplay);
    Check((await accounting.ListJournalsAsync()).Count == 1 &&
          (await reporting.SummaryAsync()).PaymentsCaptured == 1,
        "Inbox ignores replay of the same delivery id");

    var duplicateSemantic = OutboxMessage.Create(eventForReplay.Read<PaymentCapturedV1>(), clock.UtcNow);
    await paymentToAccounting.HandleAsync(duplicateSemantic);
    await paymentToEnrollment.HandleAsync(duplicateSemantic);
    await reportingHandler.HandleAsync(duplicateSemantic);
    Check((await accounting.ListJournalsAsync()).Count == 1 &&
          (await reporting.SummaryAsync()).PaymentsCaptured == 1,
        "Domain state prevents duplicate event with a new envelope id");

    var bob = await identity.RegisterAsync("bob@example.org");
    var bobEnrollment = await enrollment.RequestAsync(bob.UserId, Course.DemoCourseId);
    await payments.CaptureAsync(bobEnrollment.EnrollmentId, "payment-for-bob");
    await DrainAsync(dispatcher);
    Check((await enrollment.GetAsync(bobEnrollment.EnrollmentId))!.Status == "Enrolled", "Second seat permitted");

    var charlie = await identity.RegisterAsync("charlie@example.org");
    var charlieEnrollment = await enrollment.RequestAsync(charlie.UserId, Course.DemoCourseId);
    await payments.CaptureAsync(charlieEnrollment.EnrollmentId, "payment-for-charlie");
    await DrainAsync(dispatcher);
    Check((await enrollment.GetAsync(charlieEnrollment.EnrollmentId))!.Status == "Waitlisted", "Capacity owned by Enrollment");
    var finalReport = await reporting.SummaryAsync();
    Check(finalReport.UsersRegistered == 3 && finalReport.EnrollmentsActivated == 2 &&
          finalReport.EnrollmentsWaitlisted == 1 && finalReport.JournalsPosted == 3 &&
          finalReport.RevenueCapturedMinor == 5997,
        "Projection covers all public cross-context events");

    // A poison consumer must trigger retry, dead-letter, and explicit operator replay.
    var fakeClock = new MutableClock();
    var poisonPath = Path.Combine(dir, "poison.json");
    var poisonStore = new DurableContextStore<PoisonData>("Poison", poisonPath, fakeClock);
    var poisonEvent = OutboxMessage.Create(
        new PaymentCapturedV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1999, "USD", fakeClock.UtcNow),
        fakeClock.UtcNow);
    await poisonStore.ExecuteAsync((data, outbox) =>
    {
        outbox.Add(poisonEvent);
        return 0;
    });
    var failingDispatcher = new OutboxDispatcher([poisonStore], [new AlwaysFailHandler()]);
    for (var attempt = 0; attempt < 5; attempt++)
    {
        await failingDispatcher.DispatchOnceAsync();
        fakeClock.Advance(TimeSpan.FromSeconds(65));
    }
    Check((await poisonStore.PeekDueAsync(10)).Count == 0 &&
          (await File.ReadAllTextAsync(poisonPath)).Contains("\"deadLettered\": true"),
          "Repeated failure moves event to durable local dead letter");
    Check(await poisonStore.ReplayDeadLetterAsync(poisonEvent.Id) &&
          (await poisonStore.PeekDueAsync(10)).Count == 1,
          "Dead letter can be explicitly replayed");

    var restored = new AccountingService(new DurableContextStore<AccountingData>(
        "Accounting", Path.Combine(dir, "accounting.json"), clock));
    Check((await restored.ListJournalsAsync()).Count == 3, "State and inbox survive a new store instance");

    await ExpectDomainFailureAsync(() => enrollment.RequestAsync(Guid.NewGuid(), Course.DemoCourseId),
        "Unknown user rejected");
    await ExpectDomainFailureAsync(() => identity.RegisterAsync("alice@example.org"),
        "Duplicate identity rejected");
    await ExpectDomainFailureAsync(() => payments.CaptureAsync(bobEnrollment.EnrollmentId, "payment-for-alice"),
        "Idempotency key cannot be reused across enrollments");
    try
    {
        Journal.PostPayment(Guid.NewGuid(), 0, clock.UtcNow);
        throw new Exception("Expected invalid amount to fail.");
    }
    catch (DomainRuleException)
    {
        Console.WriteLine("PASS: Journal rejects invalid amount");
    }

    Console.WriteLine("ALL PHASE-1 CONSOLE SMOKE CHECKS PASSED");
}
finally
{
    Directory.Delete(dir, recursive: true);
}

static void Check(bool success, string description)
{
    if (!success) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS: " + description);
}

static async Task DrainAsync(OutboxDispatcher dispatcher)
{
    for (int i = 0; i < 12; i++)
    {
        if (await dispatcher.DispatchOnceAsync() == 0) return;
    }
    throw new Exception("Outbox did not drain within 12 passes.");
}

static async Task ExpectDomainFailureAsync(Func<Task> action, string description)
{
    try
    {
        await action();
        throw new Exception("FAILED: " + description);
    }
    catch (DomainRuleException)
    {
        Console.WriteLine("PASS: " + description);
    }
}

public sealed class PoisonData
{
    public int Total { get; set; }
}

public sealed class MutableClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan period) => UtcNow = UtcNow.Add(period);
}

public sealed class AlwaysFailHandler : IIntegrationEventHandler
{
    public bool CanHandle(string eventType) => eventType == EventType.Of<PaymentCapturedV1>();
    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated poison-message failure.");
}
