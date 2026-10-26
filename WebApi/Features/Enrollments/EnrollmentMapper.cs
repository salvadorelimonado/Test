using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Features.Enrollments.Create;
using BusinessLayer.Features.Enrollments.Waitlist;
using WebApi.Features.Enrollments.Cancel;
using WebApi.Features.Enrollments.Create;
using WebApi.Features.Enrollments.Waitlist;
using BusinessCard = BusinessLayer.Features.Enrollments.EnrollmentCard;

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

    public static EnrollmentCard ToResponse(BusinessCard card)
    {
        return new EnrollmentCard
        {
            Id = card.Id,
            Status = card.Status,
            Seats = card.Seats,
            CreatedAt = card.CreatedAt,
            Class = new EnrollmentCardClass
            {
                Id = card.Class.Id,
                Title = card.Class.Title,
                StartsAt = card.Class.StartsAt,
                DurationMinutes = card.Class.DurationMinutes,
                IsCancelled = card.Class.IsCancelled,
                Room = new EnrollmentCardRoom
                {
                    Id = card.Class.Room.Id,
                    Name = card.Class.Room.Name
                }
            },
            Pass = new EnrollmentCardPass
            {
                RemainingVisits = card.Pass.RemainingVisits,
                ValidUntil = card.Pass.ValidUntil
            }
        };
    }
}
