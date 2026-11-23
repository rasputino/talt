using System.Numerics;

namespace Talt.Client.Audio;

/// <summary>State of one vehicle voice after the mixer chose the eight nearest vehicles.</summary>
internal readonly record struct VoiceSlot(int VehicleId, float Distance, float Gain, float Pan, bool Active)
{
    public static VoiceSlot Empty => new(-1, 0f, 0f, 0f, false);
}

/// <summary>State of one one-shot effect voice.</summary>
internal readonly record struct OneShotSlot(AudioCue Cue, int Priority, float Gain, bool Active)
{
    public static OneShotSlot Empty => new(default, 0, 0f, false);
}

/// <summary>
/// Pure mixing policy: bus gains, distance/zoom/pan curves, the eight-vehicle voice budget with
/// priority and hysteresis, and the one-shot drop policy. It never opens a device, never allocates
/// during <see cref="UpdateVehicles"/>, and is fully unit-testable.
/// </summary>
internal sealed class AudioMixer
{
    private readonly AudioDefinition _definition;
    private readonly VoiceSlot[] _vehicleSlots;
    private readonly OneShotSlot[] _oneShots;
    private readonly int[] _candidateIds;
    private readonly float[] _candidateDistances;
    private readonly float[] _candidateX;
    private readonly float[] _candidateZ;
    private readonly float[] _candidateScores;
    private readonly VoiceSlot[] _candidateSlots;
    private readonly VoiceSlot[] _nextVehicleSlots;

    private float _master = 0.8f;
    private float _music = 0.5f;
    private float _effects = 0.8f;
    private float _ambient = 0.65f;
    private bool _muted;

    public AudioMixer(AudioDefinition definition, PlayerSettings settings)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentNullException.ThrowIfNull(settings);
        _vehicleSlots = new VoiceSlot[definition.VoiceBudget];
        _oneShots = new OneShotSlot[definition.OneShotVoices];
        _candidateIds = new int[definition.VoiceBudget];
        _candidateDistances = new float[definition.VoiceBudget];
        _candidateX = new float[definition.VoiceBudget];
        _candidateZ = new float[definition.VoiceBudget];
        _candidateScores = new float[definition.VoiceBudget];
        _candidateSlots = new VoiceSlot[definition.VoiceBudget];
        _nextVehicleSlots = new VoiceSlot[definition.VoiceBudget];
        ClearVehicleSlots();
        ClearOneShotsCore();
        ApplySettings(settings);
    }

    public ReadOnlySpan<VoiceSlot> VehicleSlots => _vehicleSlots;

    public ReadOnlySpan<OneShotSlot> OneShots => _oneShots;

    /// <summary>Copies the player's four volumes and mute into the mixer.</summary>
    public void ApplySettings(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _master = Clamp(settings.MasterVolume);
        _music = Clamp(settings.MusicVolume);
        _effects = Clamp(settings.EffectsVolume);
        _ambient = Clamp(settings.AmbientVolume);
        _muted = settings.Muted;
    }

    /// <summary>Master gain after mute; 0–1.</summary>
    public float MasterGain => _muted ? 0f : _master;

    /// <summary>
    /// Gain for one native bus. Child buses hold only their own volume because miniaudio multiplies
    /// the master group into them; the master bus holds mute times master.
    /// </summary>
    public float BusGain(AudioBus bus) => bus switch
    {
        AudioBus.Master => MasterGain,
        AudioBus.Music => _music,
        AudioBus.Effects => _effects,
        AudioBus.Ambient => _ambient,
        _ => 0f,
    };

    /// <summary>Total gain a voice on a child bus receives, including master and mute.</summary>
    public float EffectiveGain(AudioBus bus) =>
        bus == AudioBus.Master ? MasterGain : MasterGain * BusGain(bus);

    /// <summary>
    /// Chooses up to <see cref="AudioDefinition.VoiceBudget"/> vehicles within earshot. Selected and
    /// hovered vehicles win; active vehicles get a small hysteresis bonus so the mix does not churn.
    /// </summary>
    public void UpdateVehicles(in AudioScene scene)
    {
        if (!scene.InGame)
        {
            ClearVehicleSlots();
            return;
        }

        var vehicles = scene.Vehicles;
        var ids = scene.VehicleIds;
        var count = Math.Min(vehicles.Length, ids.Length);
        var maxDistance = _definition.MaxVehicleDistanceTiles;
        var maxDistanceSquared = maxDistance * maxDistance;
        var candidateCount = 0;
        var budget = _vehicleSlots.Length;
        var vehicle = _definition.Vehicle;

        for (var i = 0; i < count; i++)
        {
            var position = vehicles[i].Position;
            var dx = position.X - scene.CameraTarget.X;
            var dz = position.Z - scene.CameraTarget.Z;
            var distanceSquared = dx * dx + dz * dz;
            if (distanceSquared > maxDistanceSquared)
            {
                continue;
            }

            var id = ids[i];
            var distance = MathF.Sqrt(distanceSquared);
            var score = distance;
            if (id == scene.SelectedVehicleId)
            {
                score -= 1_000_000f;
            }
            else if (id == scene.HoveredVehicleId)
            {
                score -= 500_000f;
            }

            if (IsActiveVehicle(id))
            {
                score -= _definition.HysteresisTiles;
            }

            var positionInCandidates = candidateCount < budget ? candidateCount : budget - 1;
            if (candidateCount == budget && score >= _candidateScores[budget - 1])
            {
                continue;
            }

            while (positionInCandidates > 0 && score < _candidateScores[positionInCandidates - 1])
            {
                _candidateIds[positionInCandidates] = _candidateIds[positionInCandidates - 1];
                _candidateDistances[positionInCandidates] = _candidateDistances[positionInCandidates - 1];
                _candidateX[positionInCandidates] = _candidateX[positionInCandidates - 1];
                _candidateZ[positionInCandidates] = _candidateZ[positionInCandidates - 1];
                _candidateScores[positionInCandidates] = _candidateScores[positionInCandidates - 1];
                positionInCandidates--;
            }

            _candidateIds[positionInCandidates] = id;
            _candidateDistances[positionInCandidates] = distance;
            _candidateX[positionInCandidates] = dx;
            _candidateZ[positionInCandidates] = dz;
            _candidateScores[positionInCandidates] = score;
            if (candidateCount < budget)
            {
                candidateCount++;
            }
        }

        var forward = Flatten(scene.CameraForward);
        var right = Flatten(scene.CameraRight);
        var zoom = Math.Clamp(vehicle.ZoomReference / MathF.Max(scene.CameraDistance, 1f), vehicle.MinZoomGain, 1f);
        for (var i = 0; i < candidateCount; i++)
        {
            var distance = _candidateDistances[i];
            var dx = _candidateX[i];
            var dz = _candidateZ[i];
            var inverseDistance = distance > 0.001f ? 1f / distance : 0f;
            var forwardDot = (dx * forward.X + dz * forward.Z) * inverseDistance;
            var behind = forwardDot < 0f ? vehicle.BehindGain : 1f;
            var attenuation = distance <= vehicle.FullVolumeDistanceTiles
                ? 1f
                : vehicle.FullVolumeDistanceTiles / (vehicle.FullVolumeDistanceTiles + ((distance - vehicle.FullVolumeDistanceTiles) * vehicle.Rolloff));
            var pan = Math.Clamp((dx * right.X + dz * right.Z) * inverseDistance * vehicle.PanWidth, -1f, 1f);
            _candidateSlots[i] = new VoiceSlot(
                _candidateIds[i],
                distance,
                attenuation * zoom * behind,
                pan,
                true);
        }

        // Keep the same voice slot for a vehicle that stays selected. Ordering by rank would make
        // every rank change stop and restart two engines, which is heard as crackle.
        for (var i = 0; i < budget; i++)
        {
            _nextVehicleSlots[i] = VoiceSlot.Empty;
        }

        Span<bool> placed = stackalloc bool[budget];
        for (var i = 0; i < candidateCount; i++)
        {
            var slot = FindVehicleSlot(_candidateSlots[i].VehicleId);
            if (slot < 0 || placed[slot])
            {
                continue;
            }

            _nextVehicleSlots[slot] = _candidateSlots[i];
            placed[slot] = true;
        }

        for (var i = 0; i < candidateCount; i++)
        {
            var id = _candidateSlots[i].VehicleId;
            var alreadyPlaced = false;
            for (var slot = 0; slot < budget; slot++)
            {
                if (_nextVehicleSlots[slot].Active && _nextVehicleSlots[slot].VehicleId == id)
                {
                    alreadyPlaced = true;
                    break;
                }
            }

            if (alreadyPlaced)
            {
                continue;
            }

            for (var slot = 0; slot < budget; slot++)
            {
                if (!placed[slot])
                {
                    _nextVehicleSlots[slot] = _candidateSlots[i];
                    placed[slot] = true;
                    break;
                }
            }
        }

        for (var i = 0; i < budget; i++)
        {
            _vehicleSlots[i] = _nextVehicleSlots[i];
        }
    }

    private int FindVehicleSlot(int vehicleId)
    {
        for (var i = 0; i < _vehicleSlots.Length; i++)
        {
            if (_vehicleSlots[i].Active && _vehicleSlots[i].VehicleId == vehicleId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Reserves a one-shot voice, replacing the lowest-priority one if all are busy.</summary>
    /// <returns>The slot index, or -1 when the new sound loses to every active one.</returns>
    public int RequestOneShot(AudioCue cue, int priority, float gain)
    {
        var target = -1;
        for (var i = 0; i < _oneShots.Length; i++)
        {
            if (!_oneShots[i].Active)
            {
                target = i;
                break;
            }
        }

        if (target < 0)
        {
            var lowest = _oneShots[0].Priority;
            target = 0;
            for (var i = 1; i < _oneShots.Length; i++)
            {
                if (_oneShots[i].Priority < lowest)
                {
                    lowest = _oneShots[i].Priority;
                    target = i;
                }
            }

            if (priority <= lowest)
            {
                return -1;
            }
        }

        _oneShots[target] = new OneShotSlot(cue, priority, gain, true);
        return target;
    }

    public void ReleaseOneShot(int index)
    {
        if ((uint)index < (uint)_oneShots.Length)
        {
            _oneShots[index] = OneShotSlot.Empty;
        }
    }

    /// <summary>Clears vehicle slots when a game ends.</summary>
    public void ClearVehicles() => ClearVehicleSlots();

    /// <summary>Clears one-shot slots when a game ends.</summary>
    public void ClearOneShots() => ClearOneShotsCore();

    /// <summary>Distance curve exposed for tests and documentation.</summary>
    public static float Attenuation(float distance, float fullVolumeDistance, float rolloff)
    {
        if (distance <= fullVolumeDistance)
        {
            return 1f;
        }

        var full = MathF.Max(fullVolumeDistance, 0.001f);
        return full / (full + ((distance - full) * MathF.Max(rolloff, 0.001f)));
    }

    private bool IsActiveVehicle(int id)
    {
        for (var i = 0; i < _vehicleSlots.Length; i++)
        {
            if (_vehicleSlots[i].Active && _vehicleSlots[i].VehicleId == id)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearVehicleSlots()
    {
        for (var i = 0; i < _vehicleSlots.Length; i++)
        {
            _vehicleSlots[i] = VoiceSlot.Empty;
        }
    }

    private void ClearOneShotsCore()
    {
        for (var i = 0; i < _oneShots.Length; i++)
        {
            _oneShots[i] = OneShotSlot.Empty;
        }
    }

    private static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    private static Vector3 Flatten(Vector3 value)
    {
        var flat = new Vector3(value.X, 0f, value.Z);
        var length = flat.Length();
        return length > 0.0001f ? flat / length : Vector3.UnitZ;
    }
}
