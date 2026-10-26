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
        var row = await _unitOfWork.Enrollment.FindCardAsync(query.EnrollmentId, query.UserId, ct);
        if (row is null)
        {
            return null;
        }

        return EnrollmentCard.Create(row);
    }
}
