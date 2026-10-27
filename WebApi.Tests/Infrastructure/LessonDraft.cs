using DataAccessLayer.Entities;

namespace WebApi.Tests.Infrastructure;

public sealed class LessonDraft
{
    private const int DefaultCapacity = 8;
    private const int DefaultLeadDays = 30;

    private readonly Club _club;
    private readonly List<PassSetup> _passes = [];
    private readonly List<SeatSetup> _taken = [];
    private int _capacity = DefaultCapacity;
    private DateTime _startsAt;
    private long? _roomId;
    private bool _cancelled;

    public LessonDraft(Club club)
    {
        _club = club;
        _startsAt = club.Now.AddDays(DefaultLeadDays);
    }

    public LessonDraft Capacity(int capacity)
    {
        _capacity = capacity;
        return this;
    }

    public LessonDraft StartsAt(DateTime startsAt)
    {
        _startsAt = startsAt;
        return this;
    }

    public LessonDraft Room(long roomId)
    {
        _roomId = roomId;
        return this;
    }

    public LessonDraft Cancelled()
    {
        _cancelled = true;
        return this;
    }

    public LessonDraft Pass(int userNumber, int visits, DateOnly? validUntil = null)
    {
        var passValidityDays = 30;
        _passes.Add(new PassSetup(userNumber, visits, validUntil ?? _club.Today.AddDays(passValidityDays)));
        return this;
    }

    public LessonDraft TakenBy(int userNumber, int seats)
    {
        _taken.Add(new SeatSetup(userNumber, seats));
        return this;
    }

    public Task<Lesson> Create()
    {
        if (_passes.Count == 0)
        {
            throw new InvalidOperationException("A lesson needs a pass.");
        }

        return _club.CreateLesson(_capacity, _startsAt, _roomId, _cancelled, _passes, _taken);
    }

    internal sealed record PassSetup(int UserNumber, int Visits, DateOnly ValidUntil);

    internal sealed record SeatSetup(int UserNumber, int Seats);
}
