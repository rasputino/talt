using Talt.Core.Saves;

namespace Talt.Core.Simulation;

public sealed partial class VehicleFleet
{
    /// <summary>
    /// Writes the prototype fleet of map-less scenarios verbatim (feature 014, research R1). Its network is immutable and
    /// rebuilt from the settings (research R2).
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteArray(_lineEdges);
        writer.WriteArray(_distanceToStop);
        writer.WriteInt(_lineEdgeCount);
        writer.WriteArray(_lineStart);
        writer.WriteArray(_lineLength);
        writer.WriteArray(_lineReturn);
        writer.WriteArray(_id);
        writer.WriteArray(_state);
        writer.WriteInt(_nextVehicleId);
        writer.WriteLong(_totalDistance);
        writer.WriteInt(VehicleCount);
        writer.WriteInt(LineCount);
        writer.WriteDictionary(_slotById);
    }

    internal void ReadState(SaveReader reader)
    {
        _lineEdges = reader.ReadRequiredArray<int>();
        _distanceToStop = reader.ReadRequiredArray<long>();
        _lineEdgeCount = reader.ReadInt(0, _lineEdges.Length);
        _lineStart = reader.ReadRequiredArray<int>();
        _lineLength = reader.ReadRequiredArray<int>();
        _lineReturn = reader.ReadRequiredArray<int>();
        _id = reader.ReadRequiredArray<int>();
        _state = reader.ReadRequiredArray<VehicleState>();
        _nextVehicleId = reader.ReadInt();
        _totalDistance = reader.ReadLong();
        VehicleCount = reader.ReadInt(0, Math.Min(_id.Length, _state.Length));
        LineCount = reader.ReadInt(0, Math.Min(_lineStart.Length, Math.Min(_lineLength.Length, _lineReturn.Length)));
        reader.ReadDictionaryInto(_slotById);
    }
}
