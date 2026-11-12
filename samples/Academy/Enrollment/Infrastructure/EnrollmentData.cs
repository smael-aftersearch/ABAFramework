using Academy.Enrollment.Domain;

namespace Academy.Enrollment.Infrastructure;

public sealed class EnrollmentData
{
    public List<Course> Courses { get; set; } = [Course.Demo()];
    public List<EnrollmentAggregate> Enrollments { get; set; } = [];
}
