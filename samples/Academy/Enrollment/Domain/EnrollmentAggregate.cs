using Aba.Foundation;

namespace Academy.Enrollment.Domain;

public enum EnrollmentStatus { PendingPayment, Enrolled, Waitlisted }

public sealed record Course(Guid Id, string Name, long PriceMinor, string Currency, int Capacity)
{
    public static readonly Guid DemoCourseId = Guid.Parse("dc1311ec-bbd1-4ed7-a18c-886ff51f41b3");
    public static Course Demo() => new(DemoCourseId, "ABA Fundamentals", 1999, "USD", 2);
}

public sealed class EnrollmentAggregate
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public long PriceMinor { get; set; }
    public string Currency { get; set; } = "USD";
    public EnrollmentStatus Status { get; set; }
    public Guid? PaymentId { get; set; }

    public static EnrollmentAggregate Request(Course course, Guid userId)
    {
        if (userId == Guid.Empty) throw new DomainRuleException("A user is required.");
        return new EnrollmentAggregate
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,
            UserId = userId,
            PriceMinor = course.PriceMinor,
            Currency = course.Currency,
            Status = EnrollmentStatus.PendingPayment
        };
    }

    public void ApplyPayment(Guid paymentId, long amountMinor, string currency, bool hasCapacity)
    {
        if (Status != EnrollmentStatus.PendingPayment)
            throw new DomainRuleException("Only pending enrollments accept payment completion.");
        if (PaymentId.HasValue) throw new DomainRuleException("Payment already recorded.");
        if (PriceMinor != amountMinor || Currency != currency)
            throw new DomainRuleException("Payment does not match the enrollment quote.");
        PaymentId = paymentId;
        Status = hasCapacity ? EnrollmentStatus.Enrolled : EnrollmentStatus.Waitlisted;
    }
}
