using Talt.Core.Content;

namespace Talt.Core.Stations;

/// <summary>
/// The operations vehicles use (feature 012, research R9, contracts/stations-api.md): loading waiting cargo
/// into a <see cref="CargoHold"/> and unloading it at a station, with deliveries reported for feature 011.
/// Every operation conserves cargo.
/// </summary>
public sealed partial class StationRegistry
{
    /// <summary>
    /// Loads up to <paramref name="maxAmount"/> units of a cargo waiting at the station's group, oldest first,
    /// into <paramref name="hold"/>. Marks the cargo served, records the pickup day and the vehicle's speed for
    /// the rating, and counts passengers and mail as transported for the town they came from (FR-024).
    /// </summary>
    /// <returns>Units loaded.</returns>
    public int Load(int stationId, int cargo, int maxAmount, CargoHold hold, int speedKmh)
    {
        ArgumentNullException.ThrowIfNull(hold);
        var slot = SlotOf(stationId);
        if (slot < 0 || maxAmount <= 0 || (uint)cargo >= (uint)_cargoCount)
        {
            return 0;
        }

        MarkServed(stationId, cargo);
        var cell = (_group[slot] * _cargoCount) + cargo;
        var mail = _cargoCategory[cargo] == CargoCategory.Mail;
        var loaded = 0;
        while (loaded < maxAmount && _head[cell] >= 0)
        {
            var p = _head[cell];
            var take = Math.Min(maxAmount - loaded, _pkAmount[p]);
            var pickupDay = _pkPickupDay[p] < 0 ? _today : _pkPickupDay[p];
            hold.Add(new HeldCargo(cargo, take, _pkOrigin[p], _pkSourceTown[p], _pkDestination[p], pickupDay));
            if (_pkSourceTown[p] >= 0 && _pkPickupDay[p] < 0)
            {
                _towns?.RecordTransported(_pkSourceTown[p], mail, take);
            }

            _pkAmount[p] -= take;
            loaded += take;
            if (_pkAmount[p] == 0)
            {
                UnlinkHead(cell);
            }
        }

        _waiting[cell] -= loaded;
        _pickedUpThis[cell] += loaded;
        _lastPickupDay[cell] = _today;
        _lastSpeed[cell] = speedKmh;
        return loaded;
    }

    /// <summary>
    /// Unloads a hold at a station (FR-021, FR-022): cargo the group accepts (and that was not picked up in this
    /// group) goes to consumers in the members' catchments, the unloading station's first, and is reported to
    /// <see cref="DeliveryListener"/>; the rest stays in the hold, or with <see cref="UnloadMode.Transfer"/>
    /// waits at the group with its origin and pickup day.
    /// </summary>
    public UnloadResult Unload(int stationId, CargoHold hold, UnloadMode mode)
    {
        ArgumentNullException.ThrowIfNull(hold);
        var slot = SlotOf(stationId);
        if (slot < 0 || hold.Count == 0)
        {
            return new UnloadResult(0, 0, hold.Total);
        }

        EnsureCoverage();
        var group = _group[slot];
        var delivered = 0;
        var transferred = 0;
        for (var i = hold.Count - 1; i >= 0; i--)
        {
            var part = hold[i];
            var remaining = part.Amount;
            var originSlot = SlotOf(part.Origin);
            var fromHere = originSlot >= 0 && _group[originSlot] == group;
            if (!fromHere && (_groupAccept[group] & (1UL << part.Cargo)) != 0)
            {
                remaining = DeliverToConsumers(slot, group, part, remaining);
                delivered += part.Amount - remaining;
            }

            if (remaining > 0 && mode == UnloadMode.Transfer)
            {
                Arrive(group, part.Cargo, remaining, part.Origin, part.SourceTown, part.Destination, part.PickupDay, supplied: false);
                transferred += remaining;
                remaining = 0;
            }

            hold.SetAmount(i, remaining);
        }

        return new UnloadResult(delivered, transferred, hold.Total);
    }

    /// <summary>Hands a held part to the consumers of the group, unloading station first; returns what is left.</summary>
    private int DeliverToConsumers(int unloadingSlot, int group, in HeldCargo part, int remaining)
    {
        remaining = DeliverAtStation(unloadingSlot, group, part, remaining);
        foreach (var memberId in StationsInGroup(group))
        {
            if (remaining == 0)
            {
                break;
            }

            var member = SlotOf(memberId);
            if (member != unloadingSlot)
            {
                remaining = DeliverAtStation(member, group, part, remaining);
            }
        }

        return remaining;
    }

    private int DeliverAtStation(int slot, int group, in HeldCargo part, int remaining)
    {
        var cargo = part.Cargo;
        if ((_acceptMask[slot] & (1UL << cargo)) == 0)
        {
            return remaining;
        }

        foreach (var industryId in _covIndustries[slot])
        {
            if (remaining == 0)
            {
                return 0;
            }

            var accepted = _industries.Deliver(industryId, cargo, remaining).Accepted;
            if (accepted > 0)
            {
                Report(slot, group, part, accepted, DeliveryConsumer.Industry, industryId);
                remaining -= accepted;
            }
        }

        if (remaining == 0 || _towns is null)
        {
            return remaining;
        }

        // Towns take every unit of what their buildings accept (passengers, mail, goods, food).
        var buildings = _towns.Buildings;
        foreach (var buildingId in _covBuildings[slot])
        {
            var bslot = buildings.SlotOf(buildingId);
            if (bslot >= 0 && BuildingAccepts(buildings.TypeDefinitionAt(bslot), cargo))
            {
                Report(slot, group, part, remaining, DeliveryConsumer.Town, buildings.TownAt(bslot));
                return 0;
            }
        }

        return remaining;
    }

    private bool BuildingAccepts(Towns.BuildingType type, int cargo) =>
        (cargo == _passengerCargo && type.PassengersPerMonth > 0)
        || (cargo == _mailCargo && type.MailPerMonth > 0)
        || (cargo == _goodsCargo && type.GoodsAcceptance > 0)
        || (cargo == _foodCargo && type.FoodAcceptance > 0);

    private void Report(int slot, int group, in HeldCargo part, int amount, DeliveryConsumer consumer, int consumerId)
    {
        var cell = (group * _cargoCount) + part.Cargo;
        _deliveredThis[cell] += amount;
        _totalDelivered += amount;
        DeliveryListener?.OnDelivered(new CargoDelivery(
            _owner[slot], part.Cargo, amount, part.Origin, _id[slot], part.SourceTown, part.PickupDay, _today, consumer, consumerId));
    }
}
