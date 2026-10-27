using WebApi.Tests.Infrastructure.Stores;

namespace WebApi.Tests.Infrastructure.DTO;

public sealed class LessonBuilder
{
    private const int DefaultCapacity = 8;
    private const int DefaultLeadDays = 30;

    private readonly ClassesStore _classes;
    private readonly List<PassSetup> _passes = [];
    private readonly List<SeatSetup> _taken = [];
    private int _capacity = DefaultCapacity;
    private DateTime _startsAt;
    private long? _roomId;
    private bool _cancelled;

    public LessonBuilder(ClassesStore classes)
    {
        _classes = classes;
        _startsAt = classes.Now.AddDays(DefaultLeadDays);
    }

    public LessonBuilder Capacity(int capacity)
    {
        _capacity = capacity;
        return this;
    }

    public LessonBuilder StartsAt(DateTime startsAt)
    {
        _startsAt = startsAt;
        return this;
    }

    public LessonBuilder Room(long roomId)
    {
        _roomId = roomId;
        return this;
    }

    public LessonBuilder Cancelled()
    {
        _cancelled = true;
        return this;
    }

    public LessonBuilder Pass(int userNumber, int visits, DateOnly? validUntil = null)
    {
        var passValidityDays = 30;
        _passes.Add(new PassSetup(userNumber, visits, validUntil ?? _classes.Today.AddDays(passValidityDays)));
        return this;
    }

    public LessonBuilder TakenBy(int userNumber, int seats)
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

        return _classes.CreateLesson(_capacity, _startsAt, _roomId, _cancelled, _passes, _taken);
    }

    internal sealed record PassSetup(int UserNumber, int Visits, DateOnly ValidUntil);

    internal sealed record SeatSetup(int UserNumber, int Seats);
}
