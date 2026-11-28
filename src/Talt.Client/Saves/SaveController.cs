using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using Talt.Client.Rendering;
using Talt.Core.Saves;
using Talt.Core.Simulation;

namespace Talt.Client.Saves;

/// <summary>A finished write, reported on the main thread.</summary>
internal sealed record SaveOutcome(SaveKind Kind, string Name, SaveWriteResult Result);

/// <summary>
/// Saves the running game (research R7): captures on the main thread between ticks into one reusable snapshot (≤ 100 ms,
/// no allocation once warm), then compresses and writes it on a worker while the game keeps running. One write at a
/// time; the outcome is collected on the main thread by <see cref="Poll"/>.
/// </summary>
internal sealed class SaveController : IDisposable
{
    /// <summary>The client's camera section (opaque to Core, contracts/save-sections.md).</summary>
    public static readonly SaveSectionId CameraSection = SaveSectionId.Of("CAMR");

    private readonly SaveSnapshot _snapshot = new();
    private readonly byte[] _camera = new byte[CameraBytes];
    private readonly ExtraSection[] _extras = new ExtraSection[1];
    private Task<SaveWriteResult>? _writing;
    private SaveKind _kind;
    private string _name = string.Empty;

    private const int CameraBytes = 6 * 4;

    /// <summary>True while a save is being written.</summary>
    public bool IsWriting => _writing is not null;

    /// <summary>Main-thread time of the last capture, milliseconds (autosave stall, SC-002).</summary>
    public double LastCaptureMilliseconds { get; private set; }

    /// <summary>Primes the reusable buffers on the setup worker before a large map enters the frame loop.</summary>
    public void Prepare(GameSimulation simulation)
    {
        if (simulation.Construction is { Map: var map } && Math.Max(map.Width, map.Depth) >= 1024)
        {
            SaveGame.CaptureInto(_snapshot, simulation, new SaveDetailsInput("Setup", SaveKind.Manual, DateTimeOffset.UnixEpoch, string.Empty));
        }
    }

    /// <summary>Captures <paramref name="simulation"/> now and starts writing it to <paramref name="path"/>; false while another write runs.</summary>
    public bool TryStart(GameSimulation simulation, OrbitCamera camera, string path, string name, SaveKind kind, string gameVersion)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(camera);
        if (_writing is not null)
        {
            return false;
        }

        var started = Stopwatch.GetTimestamp();
        WriteCamera(camera, _camera);
        _extras[0] = new ExtraSection(CameraSection, 1, _camera);
        SaveGame.CaptureInto(_snapshot, simulation, new SaveDetailsInput(name, kind, DateTimeOffset.UtcNow, gameVersion), _extras);
        LastCaptureMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _kind = kind;
        _name = name;
        _writing = SaveGame.WriteAsync(_snapshot, path);
        return true;
    }

    /// <summary>The finished write, if one finished since the last call (main thread).</summary>
    public SaveOutcome? Poll()
    {
        if (_writing is not { IsCompleted: true } writing)
        {
            return null;
        }

        _writing = null;
        var result = writing.IsCompletedSuccessfully
            ? writing.Result
            : new SaveWriteResult(false, string.Empty, 0, writing.Exception?.GetBaseException().Message, writing.Exception?.GetBaseException());
        return new SaveOutcome(_kind, _name, result);
    }

    /// <summary>Blocks until a running write finishes (before loading another game or quitting).</summary>
    public SaveOutcome? WaitForWrite()
    {
        try
        {
            _writing?.Wait();
        }
        catch (AggregateException)
        {
            // Reported by Poll.
        }

        return Poll();
    }

    /// <summary>Waits for a running write and returns the snapshot buffers to the pool.</summary>
    public void Dispose()
    {
        WaitForWrite();
        _snapshot.Dispose();
    }

    /// <summary>The camera as 6 little-endian floats: target x, y, z, distance, yaw, pitch.</summary>
    public static void WriteCamera(OrbitCamera camera, Span<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(camera);
        BinaryPrimitives.WriteSingleLittleEndian(bytes, camera.Target.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], camera.Target.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], camera.Target.Z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[12..], camera.Distance);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[16..], camera.Yaw);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[20..], camera.Pitch);
    }

    /// <summary>Restores a camera written by <see cref="WriteCamera"/>; false when the section is missing or malformed.</summary>
    public static bool TryReadCamera(IReadOnlyList<ExtraSection> sections, OrbitCamera camera)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(camera);
        foreach (var section in sections)
        {
            if (section.Id != CameraSection || section.Payload.Length != CameraBytes)
            {
                continue;
            }

            var bytes = section.Payload.Span;
            var target = new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(bytes),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
            var distance = BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]);
            var yaw = BinaryPrimitives.ReadSingleLittleEndian(bytes[16..]);
            var pitch = BinaryPrimitives.ReadSingleLittleEndian(bytes[20..]);
            if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z) || !float.IsFinite(distance) || !float.IsFinite(yaw) || !float.IsFinite(pitch))
            {
                return false;
            }

            camera.Target = target;
            camera.Distance = distance;
            camera.Yaw = yaw;
            camera.Pitch = pitch;
            return true;
        }

        return false;
    }
}
