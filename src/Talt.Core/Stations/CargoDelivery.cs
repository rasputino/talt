namespace Talt.Core.Stations;

/// <summary>Who accepted a delivery.</summary>
public enum DeliveryConsumer : byte
{
    Industry = 0,
    Town = 1,

    /// <summary>The stations' debug service (<c>--station-service</c>); paid only with <c>--station-service-pays</c> (feature 011).</summary>
    DebugService = 2,
}

/// <summary>How a vehicle unloads at a station (feature 012 orders).</summary>
public enum UnloadMode : byte
{
    /// <summary>Accepted cargo is delivered; the rest stays on board.</summary>
    Normal = 0,

    /// <summary>Accepted cargo is delivered; the rest waits at the station for another vehicle.</summary>
    Transfer = 1,
}

/// <summary>What an unload did, in units.</summary>
/// <param name="Delivered">Accepted by consumers (reported as deliveries).</param>
/// <param name="Transferred">Left waiting at the station with its origin (some may be lost to the waiting cap).</param>
/// <param name="Kept">Still in the hold.</param>
/// <param name="CreditEarned">Transfer credit recorded for this leg by the cargo left here (feature 013; statistics, no money).</param>
/// <param name="CreditDelivered">Transfer credit the delivered cargo carried (subtract it from the payment in vehicle profit).</param>
public readonly record struct UnloadResult(int Delivered, int Transferred, int Kept, long CreditEarned = 0, long CreditDelivered = 0);

/// <summary>
/// One accepted delivery (data-model.md CargoDelivery), with everything feature 011 needs to pay for it:
/// cargo, amount, where it was picked up and delivered, when, and who accepted it.
/// </summary>
public readonly record struct CargoDelivery(
    int CompanyId,
    int Cargo,
    int Amount,
    int OriginStation,
    int DestinationStation,
    int SourceTown,
    int PickupDay,
    int DeliveryDay,
    DeliveryConsumer ConsumerKind,
    int ConsumerId)
{
    /// <summary>Whole days between the first pickup and the delivery.</summary>
    public int DaysInTransit => Math.Max(0, DeliveryDay - PickupDay);
}

/// <summary>Receives every accepted delivery (feature 011 pays for them). Not simulation state.</summary>
public interface IDeliveryListener
{
    void OnDelivered(in CargoDelivery delivery);
}

/// <summary>
/// Estimates what a leg of a transfer is worth (feature 013, research R12): the payment the cargo would earn for the
/// distance and days so far. Feature 011's finance implements it; only vehicle profit statistics use it.
/// </summary>
public interface ITransferValuer
{
    long Estimate(int cargo, int amount, int distanceTiles, int days);
}
