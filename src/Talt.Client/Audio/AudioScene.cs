using System.Numerics;
using Talt.Client.Rendering;

namespace Talt.Client.Audio;

/// <summary>
/// Everything the audio layer needs to know about one frame (data-model.md AudioScene). Spans point
/// at the session's existing arrays, so building the scene allocates nothing.
/// </summary>
internal readonly ref struct AudioScene
{
    public bool InGame { get; init; }

    public bool Paused { get; init; }

    public Vector3 CameraPosition { get; init; }

    public Vector3 CameraTarget { get; init; }

    public Vector3 CameraForward { get; init; }

    public Vector3 CameraRight { get; init; }

    public float CameraDistance { get; init; }

    public ReadOnlySpan<VehicleInstance> Vehicles { get; init; }

    public ReadOnlySpan<int> VehicleIds { get; init; }

    public int SelectedVehicleId { get; init; }

    public int HoveredVehicleId { get; init; }

    public AmbientMap? Ambients { get; init; }

    public float DeltaSeconds { get; init; }
}
