using Talt.Core.Saves;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

public sealed partial class TrainSystem
{
    /// <summary>
    /// The <c>TRAI</c> section (feature 014, research R1): every train in slot order (parts, holds, steps, route,
    /// timers, flags), the id order of the sequential phase, tile ownership and block ownership (reservations), link tables,
    /// carries, budgets and counters. The notice feed is interface state; the router and its scratch routes are rebuilt.
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteULong(_seed);
        writer.WriteInt(_nextId);
        writer.WriteLong(_tick);
        writer.WriteInt(_searchBudget);
        writer.WriteLong(_deliveries);
        writer.WriteLong(_distance);
        writer.WriteInt(Version);
        writer.WriteLong(RouteSearches);
        writer.WriteInt(_trains.Count);
        foreach (var train in _trains)
        {
            train.WriteState(writer);
        }

        writer.WriteInt(_byId.Count);
        foreach (var train in _byId)
        {
            writer.WriteInt(train.Id);
        }

        writer.WriteDictionary(_slotById);
        var everUsed = _rails.EverUsed;
        writer.WriteBool(everUsed);
        if (everUsed)
        {
            writer.WritePages(_owner, 0);
        }

        writer.WriteArray(_blockOwned);
        writer.WriteDictionary(_lineCargo);
        writer.WriteInt(_linkChanges);
        writer.WriteArray(_maintenanceCarry);
    }

    internal void ReadState(SaveReader reader, int version)
    {
        if (reader.ReadULong() != _seed)
        {
            throw reader.Fail("train seed differs (the game was not constructed like the saved one)");
        }

        _nextId = reader.ReadInt();
        _tick = reader.ReadLong();
        _searchBudget = reader.ReadInt();
        _deliveries = reader.ReadLong();
        _distance = reader.ReadLong();
        Version = reader.ReadInt();
        RouteSearches = reader.ReadLong();
        var count = reader.ReadCount(16);
        _trains.Clear();
        var byId = new Dictionary<int, Train>();
        for (var i = 0; i < count; i++)
        {
            var train = Train.ReadState(reader);
            _trains.Add(train);
            if (!byId.TryAdd(train.Id, train))
            {
                throw reader.Fail("duplicate train id");
            }
        }

        var order = reader.ReadCount(4);
        if (order != count)
        {
            throw reader.Fail("train id order has the wrong length");
        }

        _byId.Clear();
        for (var i = 0; i < order; i++)
        {
            _byId.Add(byId.TryGetValue(reader.ReadInt(), out var train) ? train : throw reader.Fail("train id order refers to an unknown train"));
        }

        reader.ReadDictionaryInto(_slotById);
        if (reader.ReadBool())
        {
            reader.ReadPages(_owner, 0, version);
        }
        else
        {
            Array.Clear(_owner);
        }

        _blockOwned = reader.ReadRequiredArray<int>();
        reader.ReadDictionaryInto(_lineCargo);
        reader.Content.CargoKeys(_lineCargo, 0);
        _linkChanges = reader.ReadInt();
        _maintenanceCarry = reader.ReadRequiredArray<long>();
    }
}

public sealed partial class Train
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Id);
        writer.WriteInt(Owner);
        writer.WriteInt(Parts.Count);
        foreach (var part in Parts)
        {
            part.WriteState(writer);
        }

        writer.WriteInt(Line);
        writer.WriteInt(StopIndex);
        writer.WriteInt(Depot);
        writer.WriteInt(HomeDepot);
        writer.Write(Mode);
        writer.Write(Flags);
        writer.Write(Orders);
        writer.Write(TargetKind);
        writer.WriteInt(TargetId);
        writer.WriteInt(Speed);
        writer.WriteInt(HeadStep);
        writer.WriteInt(HeadOffset);
        Route.WriteState(writer);
        writer.WriteInt(RoutePos);
        writer.WriteInt(Timer);
        writer.WriteULong(ServedMask);
        writer.WriteInt(WaitTicks);
        writer.WriteLong(LastPlanTick);
        writer.WriteInt(AgeDays);
        writer.WriteInt(Reliability);
        writer.WriteInt(DaysSinceService);
        writer.WriteInt(ServiceInterval);
        writer.WriteInt(BreakdownTicks);
        writer.WriteLong(ProfitThisYear);
        writer.WriteLong(ProfitLastYear);
        writer.WriteInt(Length);
        writer.WriteInt(MassTonnes);
        writer.WriteInt(PowerKw);
        writer.WriteInt(EffortKn);
        writer.WriteInt(MaxSpeed);
        writer.WriteBool(Electric);
        writer.WriteArray(_stepFrom);
        writer.WriteArray(_stepDir);
        writer.WriteArray(_stepLen);
        writer.WriteInt(_stepStart);
        writer.WriteInt(StepCount);
        writer.WriteInt(EndTile);
        writer.WriteLong(TotalLength);
    }

    internal static Train ReadState(SaveReader reader)
    {
        var train = new Train(reader.ReadInt(), reader.ReadInt(), depot: -1);
        var parts = reader.ReadCount(8);
        for (var i = 0; i < parts; i++)
        {
            train.Parts.Add(TrainPart.ReadState(reader));
        }

        train.Line = reader.ReadInt();
        train.StopIndex = reader.ReadInt();
        train.Depot = reader.ReadInt();
        train.HomeDepot = reader.ReadInt();
        train.Mode = reader.Read<TrainMode>();
        train.Flags = reader.Read<TrainBits>();
        train.Orders = reader.Read<VehicleOrders>();
        train.TargetKind = reader.Read<RouteGoalKind>();
        train.TargetId = reader.ReadInt();
        train.Speed = reader.ReadInt();
        train.HeadStep = reader.ReadInt();
        train.HeadOffset = reader.ReadInt();
        train.Route.ReadState(reader);
        train.RoutePos = reader.ReadInt();
        train.Timer = reader.ReadInt();
        train.ServedMask = reader.ReadULong();
        train.WaitTicks = reader.ReadInt();
        train.LastPlanTick = reader.ReadLong();
        train.AgeDays = reader.ReadInt();
        train.Reliability = reader.ReadInt();
        train.DaysSinceService = reader.ReadInt();
        train.ServiceInterval = reader.ReadInt();
        train.BreakdownTicks = reader.ReadInt();
        train.ProfitThisYear = reader.ReadLong();
        train.ProfitLastYear = reader.ReadLong();
        train.Length = reader.ReadInt();
        train.MassTonnes = reader.ReadInt();
        train.PowerKw = reader.ReadInt();
        train.EffortKn = reader.ReadInt();
        train.MaxSpeed = reader.ReadInt();
        train.Electric = reader.ReadBool();
        train._stepFrom = reader.ReadRequiredArray<int>();
        train._stepDir = reader.ReadRequiredArray<byte>();
        train._stepLen = reader.ReadRequiredArray<int>();
        train._stepStart = reader.ReadInt();
        train.StepCount = reader.ReadInt();
        train.EndTile = reader.ReadInt();
        train.TotalLength = reader.ReadLong();
        var size = train._stepFrom.Length;
        if (size == 0 || (size & (size - 1)) != 0 || train._stepDir.Length != size || train._stepLen.Length != size || train.StepCount < 0 || train.StepCount > size)
        {
            throw reader.Fail("train step ring is inconsistent");
        }

        return train;
    }
}

public sealed partial class TrainPart
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Model);
        writer.WriteInt(Cargo);
        writer.WriteInt(RunningHandle);
        writer.WriteInt(PurchaseDay);
        writer.WriteLong(Price);
        Hold.WriteState(writer);
    }

    internal static TrainPart ReadState(SaveReader reader)
    {
        var model = reader.Content.Map(ContentTable.RollingStockModel, reader.ReadInt());
        var part = new TrainPart(model, reader.Content.Map(ContentTable.Cargo, reader.ReadInt()), reader.ReadInt(), reader.ReadInt(), reader.ReadLong());
        part.Hold.ReadState(reader);
        return part;
    }
}

public sealed partial class RailRoute
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Count);
        writer.WriteArray(_from);
        writer.WriteArray(_dir);
    }

    internal void ReadState(SaveReader reader)
    {
        var count = reader.ReadInt();
        _from = reader.ReadRequiredArray<int>();
        _dir = reader.ReadRequiredArray<byte>();
        if (count < 0 || count > _from.Length || _dir.Length != _from.Length)
        {
            throw reader.Fail("train route is inconsistent");
        }

        Count = count;
    }
}
