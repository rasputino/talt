using Talt.Core.Determinism;

namespace Talt.Core.Finance;

/// <summary>
/// Yearly running costs of registered vehicles (feature 011, data-model.md RunningCosts, FR-011): feature 012
/// registers each vehicle with its yearly cost and value; the finance system charges the per-company totals
/// a little every day. Handles are stable; freed slots are reused lowest first so the order is deterministic.
/// </summary>
public sealed class RunningCosts : IStateHashable
{
    private int[] _company = new int[16];
    private long[] _yearlyCost = new long[16];
    private long[] _value = new long[16];
    private bool[] _active = new bool[16];
    private readonly SortedSet<int> _free = [];
    private long[] _yearlyByCompany = new long[1];
    private long[] _valueByCompany = new long[1];
    private int _count;

    /// <summary>Registrations in use.</summary>
    public int ActiveCount { get; private set; }

    /// <summary>Raised when the first registration is made (turns finance hashing on).</summary>
    internal Action? Activated { get; set; }

    /// <summary>Registers a vehicle; returns its handle.</summary>
    public int Register(int companyId, long yearlyCost, long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(companyId);
        ArgumentOutOfRangeException.ThrowIfNegative(yearlyCost);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        int handle;
        if (_free.Count > 0)
        {
            handle = _free.Min;
            _free.Remove(handle);
        }
        else
        {
            handle = _count++;
            if (handle == _company.Length)
            {
                var size = handle * 2;
                Array.Resize(ref _company, size);
                Array.Resize(ref _yearlyCost, size);
                Array.Resize(ref _value, size);
                Array.Resize(ref _active, size);
            }
        }

        EnsureCompany(companyId);
        _company[handle] = companyId;
        _yearlyCost[handle] = yearlyCost;
        _value[handle] = value;
        _active[handle] = true;
        _yearlyByCompany[companyId] = Money.Add(_yearlyByCompany[companyId], yearlyCost);
        _valueByCompany[companyId] = Money.Add(_valueByCompany[companyId], value);
        ActiveCount++;
        Activated?.Invoke();
        return handle;
    }

    /// <summary>Removes a registration (a sold or scrapped vehicle).</summary>
    public void Unregister(int handle)
    {
        if ((uint)handle >= (uint)_count || !_active[handle])
        {
            throw new ArgumentOutOfRangeException(nameof(handle), handle, "No running cost is registered with this handle.");
        }

        var company = _company[handle];
        _yearlyByCompany[company] = Money.Add(_yearlyByCompany[company], -_yearlyCost[handle]);
        _valueByCompany[company] = Money.Add(_valueByCompany[company], -_value[handle]);
        _active[handle] = false;
        _free.Add(handle);
        ActiveCount--;
    }

    /// <summary>Updates a replacement in its existing registration without releasing the handle.</summary>
    internal void Replace(int handle, long yearlyCost, long value)
    {
        SetValue(handle, value);
        var company = _company[handle];
        _yearlyByCompany[company] = Money.Add(_yearlyByCompany[company], yearlyCost - _yearlyCost[handle]);
        _yearlyCost[handle] = yearlyCost;
    }

    /// <summary>Changes the value of a registration (vehicles lose value with age, feature 012).</summary>
    public void SetValue(int handle, long value)
    {
        if ((uint)handle >= (uint)_count || !_active[handle])
        {
            throw new ArgumentOutOfRangeException(nameof(handle), handle, "No running cost is registered with this handle.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(value);
        var company = _company[handle];
        _valueByCompany[company] = Money.Add(_valueByCompany[company], value - _value[handle]);
        _value[handle] = value;
    }

    /// <summary>Sum of the yearly costs of a company's registrations (before inflation).</summary>
    public long YearlyTotal(int companyId) => (uint)companyId < (uint)_yearlyByCompany.Length ? _yearlyByCompany[companyId] : 0;

    /// <summary>Sum of the values of a company's registrations.</summary>
    public long ValueTotal(int companyId) => (uint)companyId < (uint)_valueByCompany.Length ? _valueByCompany[companyId] : 0;

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(_count);
        for (var i = 0; i < _count; i++)
        {
            hasher.Append(_active[i] ? 1 : 0);
            if (_active[i])
            {
                hasher.Append(_company[i]);
                hasher.Append(_yearlyCost[i]);
                hasher.Append(_value[i]);
            }
        }
    }

    private void EnsureCompany(int companyId)
    {
        if (companyId >= _yearlyByCompany.Length)
        {
            Array.Resize(ref _yearlyByCompany, companyId + 1);
            Array.Resize(ref _valueByCompany, companyId + 1);
        }
    }
}
