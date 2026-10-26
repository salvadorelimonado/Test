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
        var card = await _cardCache.GetOrLoadAsync(
            command.UserId,
            command.EnrollmentId,
            token => LoadAsync(command, token),
            ct);

        if (card is null)
        {
            return GetEnrollmentResult.NotFound();
        }

        return GetEnrollmentResult.Found(card);
    }

    private async Task<EnrollmentCard?> LoadAsync(GetEnrollmentCommand command, CancellationToken ct)
    {
        var enrollment = await _unitOfWork.Enrollment.GetByIdAsync(command.EnrollmentId, ct);
        if (enrollment is null || enrollment.UserId != command.UserId)
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

        var pass = await _unitOfWork.Pass.GetByUserAsync(command.UserId, ct);
        if (pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        return EnrollmentCard.Create(enrollment, scheduledClass, room, pass);
    }
}
