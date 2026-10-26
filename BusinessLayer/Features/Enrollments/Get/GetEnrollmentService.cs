using BusinessLayer.Result;
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

    public async Task<OperationResult<EnrollmentCard>> GetAsync(GetEnrollmentQuery query, CancellationToken ct)
    {
        var card = await _cardCache.GetOrLoadAsync(
            query.UserId,
            query.EnrollmentId,
            token => LoadAsync(query, token),
            ct);

        if (card is null)
        {
            return new OperationResult<EnrollmentCard>.NotFound();
        }

        return new OperationResult<EnrollmentCard>.Ok(card);
    }

    private async Task<EnrollmentCard?> LoadAsync(GetEnrollmentQuery query, CancellationToken ct)
    {
        var enrollment = await _unitOfWork.Enrollment.GetByIdAsync(query.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != query.UserId)
        {
            return null;
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

        var pass = await _unitOfWork.Pass.GetByUserAsync(query.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        return EnrollmentCard.Create(enrollment, scheduledClass, room, pass);
    }
}
