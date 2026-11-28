using Talt.Core.Saves;

namespace Talt.Core.Vehicles;

public sealed partial class ReplacementPolicy
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_rules.Count);
        for (var i = 0; i < _rules.Count; i++)
        {
            writer.WriteString(_rules.Keys[i]);
            var rule = _rules.Values[i];
            writer.WriteString(rule.Target);
            writer.WriteBool(rule.EndOfLifeOnly);
            writer.WriteBool(rule.Renew);
        }
    }

    internal void ReadState(SaveReader reader, Func<string, int> findModel)
    {
        var count = reader.ReadCount(10);
        for (var i = 0; i < count; i++)
        {
            var source = reader.ReadRequiredString();
            var target = reader.ReadRequiredString();
            if (findModel(source) < 0 || (target.Length > 0 && findModel(target) < 0))
            {
                throw reader.Fail($"Invalid replacement rule from {source} to {target}.");
            }

            Set(source, new ReplacementRule(target, reader.ReadBool(), reader.ReadBool()));
        }
    }
}
