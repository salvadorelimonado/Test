using BusinessLayer.Result;
using DataAccessLayer.Repositories.Projections;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Enrollments.Get;

public sealed class GetEnrollmentHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly EnrollmentCardCache _cardCache;

    public GetEnrollmentHandler(IUnitOfWork unitOfWork, EnrollmentCardCache cardCache)
    {
        _unitOfWork = unitOfWork;
        _cardCache = cardCache;
    }

    public async Task<OperationResult<EnrollmentCardRow>> GetAsync(GetEnrollmentQuery query, CancellationToken ct)
    {
        var card = await _cardCache.GetOrLoadAsync(
            query.UserId,
            query.EnrollmentId,
            token => LoadAsync(query, token),
            ct);

        if (card is null)
        {
            return new OperationResult<EnrollmentCardRow>.NotFound();
        }

        return new OperationResult<EnrollmentCardRow>.Ok(card);
    }

    private async Task<EnrollmentCardRow?> LoadAsync(GetEnrollmentQuery query, CancellationToken ct)
    {
        var row = await _unitOfWork.Enrollment.FindCardAsync(query.EnrollmentId, query.UserId, ct);
        if (row is null)
        {
            return null;
        }

        if (row.Pass is null)
        {
            throw new InvalidOperationException("Pass was not found.");
        }

        return row;
    }
}
