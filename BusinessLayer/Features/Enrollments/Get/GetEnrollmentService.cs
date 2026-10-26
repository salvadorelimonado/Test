using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.Get;

public sealed class GetEnrollmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly EnrollmentCardCache _cardCache;

    public GetEnrollmentService(IUnitOfWork unitOfWork, EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _cardCache = cardCache;
    }

    public async Task<GetEnrollmentResult> GetAsync(GetEnrollmentCommand command, CancellationToken ct)
    {
        var lookup = await _cardCache.ReadAsync(command.UserId, command.EnrollmentId, ct);
        if (lookup.Card is not null)
        {
            return GetEnrollmentResult.Found(lookup.Card);
        }

        var enrollment = await _unitOfWork.Enrollment.GetByIdAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
        {
            return GetEnrollmentResult.NotFound();
        }

        var scheduledClass = await _unitOfWork.Class.GetByIdAsync(enrollment.ClassId, ct);
        if (scheduledClass is null)
        {
            throw new InvalidOperationException("Class was not found.");
        }

        var room = await _unitOfWork.Room.GetByIdAsync(scheduledClass.RoomId, ct);
        if (room is null)
        {
            throw new InvalidOperationException("Room was not found.");
        }

        var pass = await _unitOfWork.Pass.GetByUserAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        var card = EnrollmentCard.Create(enrollment, scheduledClass, room, pass);
        await _cardCache.SetAsync(command.UserId, lookup.Generation, card, ct);
        return GetEnrollmentResult.Found(card);
    }
}
