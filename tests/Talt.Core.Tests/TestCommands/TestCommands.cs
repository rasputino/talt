using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.TestCommands;

/// <summary>Does nothing; exercises the queue and recording.</summary>
internal sealed record NoopCommand : ISimulationCommand
{
    public const string Type = "test.noop";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation) => CommandResult.Success;

    public void Apply(GameSimulation simulation)
    {
    }

    public void WriteProperties(Utf8JsonWriter writer)
    {
    }
}

/// <summary>Consumes values from a forked random stream; proves RNG use is reproducible.</summary>
internal sealed record ConsumeRandomCommand(int Count) : ISimulationCommand
{
    public const string Type = "test.consumeRandom";

    public string TypeName => Type;

    public CommandResult Validate(GameSimulation simulation) =>
        Count > 0 ? CommandResult.Success : CommandResult.Rejected("invalid-count", "Count must be positive.");

    public void Apply(GameSimulation simulation)
    {
        for (var i = 0; i < Count; i++)
        {
            simulation.Random.NextUInt64();
        }
    }

    public void WriteProperties(Utf8JsonWriter writer) => writer.WriteNumber("count", Count);
}

internal static class TestCommandRegistration
{
    private static int _registered;

    /// <summary>Registers the test-only command types with the serializer (idempotent).</summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        CommandSerializer.Register(NoopCommand.Type, _ => new NoopCommand());
        CommandSerializer.Register(ConsumeRandomCommand.Type, e => new ConsumeRandomCommand(e.GetProperty("count").GetInt32()));
    }
}
