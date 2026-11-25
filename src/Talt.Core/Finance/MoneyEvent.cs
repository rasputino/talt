namespace Talt.Core.Finance;

/// <summary>What a money event tells the client.</summary>
public enum MoneyEventKind : byte
{
    /// <summary>A delivery was paid (floating "+" text).</summary>
    Income = 0,

    /// <summary>Money was spent at a place (floating "−" text).</summary>
    Expense = 1,

    /// <summary>The company has been bankrupt for the warning number of months (or more).</summary>
    BankruptcyWarning = 2,

    /// <summary>The bankruptcy grace limit was reached: the game is over.</summary>
    GameOver = 3,
}

/// <summary>One event for the client (data-model.md MoneyEvent); (X, Z) is a tile, or −1 when it has no place.</summary>
public readonly record struct MoneyEvent(int CompanyId, long Amount, int X, int Z, MoneyEventKind Kind);

/// <summary>
/// Fixed-size feed of money events for the client (research R13). Not simulation state: written by the
/// simulation thread, drained by the client after ticks; when full the oldest event is dropped. Never allocates.
/// </summary>
public sealed class MoneyEventFeed
{
    /// <summary>Events kept before the oldest is dropped.</summary>
    public const int Capacity = 512;

    private readonly MoneyEvent[] _events = new MoneyEvent[Capacity];
    private int _head;
    private int _count;

    /// <summary>Events waiting.</summary>
    public int Count => _count;

    internal void Add(in MoneyEvent e)
    {
        var tail = (_head + _count) % Capacity;
        _events[tail] = e;
        if (_count == Capacity)
        {
            _head = (_head + 1) % Capacity;
        }
        else
        {
            _count++;
        }
    }

    /// <summary>Takes the oldest waiting event.</summary>
    public bool TryDequeue(out MoneyEvent e)
    {
        if (_count == 0)
        {
            e = default;
            return false;
        }

        e = _events[_head];
        _head = (_head + 1) % Capacity;
        _count--;
        return true;
    }

    /// <summary>Drops every waiting event (tools and tests that do not show them).</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}
