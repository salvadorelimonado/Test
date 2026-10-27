using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Features.Enrollments.Create;
using BusinessLayer.Features.Enrollments.JoinWaitlist;
using BusinessLayer.Features.Enrollments.LeaveWaitlist;
using DataAccessLayer.Repositories;
using WebApi.Features.Enrollments.Cancel;
using WebApi.Features.Enrollments.Create;
using WebApi.Features.Enrollments.Waitlist;

namespace WebApi.Features.Enrollments;

public static class EnrollmentMapper
{
    public static CreateEnrollmentResponse ToResponse(CreatedEnrollment enrollment)
    {
        return new CreateEnrollmentResponse
        {
            Id = enrollment.Id,
            ClassId = enrollment.ClassId,
            Seats = enrollment.Seats,
            Status = enrollment.Status,
            CreatedAt = enrollment.CreatedAt
        };
    }

    public static CancelEnrollmentResponse ToResponse(CancelledEnrollment enrollment)
    {
        return new CancelEnrollmentResponse
        {
            Id = enrollment.Id,
            Status = enrollment.Status,
            RefundedVisits = enrollment.RefundedVisits
        };
    }

    public static JoinWaitlistResponse ToResponse(JoinedWaitlist entry)
    {
        return new JoinWaitlistResponse
        {
            Id = entry.Id,
            ClassId = entry.ClassId,
            Seats = entry.Seats,
            Position = entry.Position,
            CreatedAt = entry.CreatedAt
        };
    }

    public static LeaveWaitlistResponse ToResponse(LeftWaitlist entry)
    {
        return new LeaveWaitlistResponse
        {
            Id = entry.Id,
            Status = entry.Status
        };
    }

    public static EnrollmentCard ToResponse(EnrollmentCardRow row)
    {
        var pass = row.Pass;
        if (pass is null)
        {
            throw new ArgumentNullException(nameof(row.Pass));
        }

        return new EnrollmentCard
        {
            Id = row.Id,
            Status = row.Status,
            Seats = row.Seats,
            CreatedAt = row.CreatedAt,
            Class = new EnrollmentCardClass
            {
                Id = row.ClassId,
                Title = row.Title,
                StartsAt = row.StartsAt,
                DurationMinutes = row.DurationMinutes,
                IsCancelled = row.IsCancelled,
                Room = new EnrollmentCardRoom
                {
                    Id = row.RoomId,
                    Name = row.RoomName
                }
            },
            Pass = new EnrollmentCardPass
            {
                RemainingVisits = pass.RemainingVisits,
                ValidUntil = pass.ValidUntil
            }
        };
    }
}
