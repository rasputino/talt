using System.Text.Json;
using Talt.Core.Saves;

namespace Talt.Core.Commands;

public sealed partial class CommandInbox
{
    /// <summary>
    /// Writes the commands waiting for the next tick and the next submission id (feature 014, research R17). Commands are
    /// rare and small, so each is written as its session JSON (the same form <see cref="CommandSerializer"/> records).
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteLong(_nextSubmissionId);
        writer.WriteInt(_pending.Count);
        foreach (var pending in _pending)
        {
            writer.WriteLong(pending.SubmissionId);
            writer.WriteInt(pending.IssuerId);
            writer.WriteCommand(pending.Command);
        }
    }

    internal void ReadState(SaveReader reader)
    {
        _nextSubmissionId = reader.ReadLong();
        var count = reader.ReadCount(16);
        _pending.Clear();
        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadLong();
            var issuer = reader.ReadInt();
            var text = reader.ReadRequiredString();
            ISimulationCommand command;
            try
            {
                using var document = JsonDocument.Parse(text);
                command = CommandSerializer.Read(document.RootElement);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new SaveFormatException($"Section {reader.Section}: a pending command cannot be read.", e);
            }

            _pending.Enqueue(new PendingCommand(id, issuer, command));
        }
    }
}
