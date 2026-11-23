#:package EggEncoder@4.114.5
#:property ManagePackageVersionsCentrally=false

// Generates every original sound and music track shipped with TALT (feature 019).
// Run from the repository root: dotnet run tools/audio/generate-audio.cs
// The output is deterministic; the WAV/OGG files are committed so the game does not need this tool.

using EggEncoder.Codecs.Flac;

const int SampleRate = 44100;
var root = Path.Combine(Directory.GetCurrentDirectory(), "src", "Talt.Client", "Assets", "Audio");
Directory.CreateDirectory(Path.Combine(root, "effects"));
Directory.CreateDirectory(Path.Combine(root, "ambient"));
Directory.CreateDirectory(Path.Combine(root, "music"));

WriteWav(Path.Combine(root, "effects", "interface-click.wav"), Click());
WriteWav(Path.Combine(root, "effects", "command-applied.wav"), Applied());
WriteWav(Path.Combine(root, "effects", "command-rejected.wav"), Rejected());
WriteWav(Path.Combine(root, "effects", "construction.wav"), Construction());
WriteFlac(Path.Combine(root, "effects", "vehicle-engine.flac"), Engine());
WriteFlac(Path.Combine(root, "ambient", "country.flac"), Country());
WriteFlac(Path.Combine(root, "ambient", "water.flac"), Water());
WriteFlac(Path.Combine(root, "ambient", "town.flac"), Town());
WriteFlac(Path.Combine(root, "music", "open-road.flac"), OpenRoad());
WriteFlac(Path.Combine(root, "music", "daybreak.flac"), Daybreak());
Console.WriteLine($"Audio assets written to {root}");

static float[][] Stereo(int samples)
{
    return [new float[samples], new float[samples]];
}

static int Samples(double seconds) => (int)(seconds * SampleRate);

static float Frequency(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

static void Add(float[][] buffer, int index, float value, float pan)
{
    if ((uint)index >= (uint)buffer[0].Length)
    {
        return;
    }

    var left = MathF.Sqrt(0.5f * (1f - pan));
    var right = MathF.Sqrt(0.5f * (1f + pan));
    buffer[0][index] += value * left;
    buffer[1][index] += value * right;
}

static void Note(float[][] buffer, double start, double duration, int midi, float amplitude, Wave wave, float pan,
    double attack = 0.01, double release = 0.15)
{
    var first = Samples(start);
    var length = Math.Max(1, Samples(duration));
    var attackSamples = Math.Max(1, Samples(attack));
    var releaseSamples = Math.Max(1, Samples(Math.Min(release, duration)));
    var phase = 0f;
    var frequency = Frequency(midi);
    for (var i = 0; i < length; i++)
    {
        var envelope = i < attackSamples ? i / (float)attackSamples : 1f;
        var left = length - i;
        if (left < releaseSamples)
        {
            envelope *= left / (float)releaseSamples;
        }

        var t = i / (float)SampleRate;
        var sample = wave(t, phase) * envelope * amplitude;
        phase += frequency / SampleRate;
        if (phase > 1f)
        {
            phase -= 1f;
        }

        Add(buffer, first + i, sample, pan);
    }
}

static void NoiseNote(float[][] buffer, double start, double duration, float amplitude, float pan, int seed,
    float lowpass = 0.15f)
{
    var first = Samples(start);
    var length = Math.Max(1, Samples(duration));
    var state = (uint)seed * 1664525u + 1013904223u;
    var filtered = 0f;
    for (var i = 0; i < length; i++)
    {
        state = (state * 1664525u) + 1013904223u;
        var white = ((state >> 8) & 0xFFFF) / 32768f - 1f;
        filtered += (white - filtered) * lowpass;
        var envelope = MathF.Min(1f, i / (SampleRate * 0.005f)) * MathF.Min(1f, (length - i) / (SampleRate * 0.02f));
        Add(buffer, first + i, filtered * amplitude * envelope, pan);
    }
}

static void Kick(float[][] buffer, double start, float amplitude)
{
    var first = Samples(start);
    var length = Samples(0.18);
    var phase = 0f;
    for (var i = 0; i < length; i++)
    {
        var t = i / (float)length;
        var frequency = 90f - (45f * t);
        phase += frequency / SampleRate;
        var value = MathF.Sin(phase * MathF.Tau) * (1f - t) * (1f - t) * amplitude;
        Add(buffer, first + i, value, 0f);
    }
}

static void Hat(float[][] buffer, double start, float amplitude)
{
    var first = Samples(start);
    var length = Samples(0.035);
    var state = (uint)(start * 1000) * 747796405u + 2891336453u;
    for (var i = 0; i < length; i++)
    {
        state = (state * 1664525u) + 1013904223u;
        var noise = ((state >> 8) & 0xFFFF) / 32768f - 1f;
        Add(buffer, first + i, noise * amplitude * (1f - (i / (float)length)), 0.15f);
    }
}

static float[][] Click()
{
    var buffer = Stereo(Samples(0.06));
    Note(buffer, 0, 0.05, 96, 0.25f, Sine, 0f, attack: 0.001, release: 0.05);
    NoiseNote(buffer, 0, 0.015, 0.04f, 0f, 11, 0.4f);
    Normalize(buffer, 0.7f);
    return buffer;
}

static float[][] Applied()
{
    var buffer = Stereo(Samples(0.5));
    Note(buffer, 0.00, 0.22, 84, 0.20f, Sine, -0.15f, attack: 0.005, release: 0.20);
    Note(buffer, 0.09, 0.35, 91, 0.20f, Sine, 0.15f, attack: 0.005, release: 0.30);
    Note(buffer, 0.09, 0.35, 96, 0.08f, Triangle, 0.15f, attack: 0.005, release: 0.30);
    Normalize(buffer, 0.65f);
    return buffer;
}

static float[][] Rejected()
{
    var buffer = Stereo(Samples(0.45));
    Note(buffer, 0.00, 0.20, 54, 0.20f, Triangle, -0.1f, attack: 0.005, release: 0.12);
    Note(buffer, 0.12, 0.28, 49, 0.20f, Triangle, 0.1f, attack: 0.005, release: 0.20);
    NoiseNote(buffer, 0.00, 0.30, 0.03f, 0f, 23, 0.08f);
    Normalize(buffer, 0.6f);
    return buffer;
}

static float[][] Construction()
{
    var buffer = Stereo(Samples(0.4));
    Note(buffer, 0.00, 0.30, 36, 0.28f, Sine, 0f, attack: 0.002, release: 0.25);
    Note(buffer, 0.00, 0.18, 43, 0.10f, Triangle, 0f, attack: 0.002, release: 0.15);
    NoiseNote(buffer, 0.02, 0.28, 0.09f, -0.2f, 37, 0.12f);
    NoiseNote(buffer, 0.08, 0.20, 0.05f, 0.25f, 41, 0.20f);
    Normalize(buffer, 0.7f);
    return buffer;
}

static float[][] Engine()
{
    var buffer = Stereo(Samples(2.0));
    var length = buffer[0].Length;
    var lcg = 0x12345678u;
    for (var i = 0; i < length; i++)
    {
        var t = i / (float)SampleRate;
        var modulation = 0.85f + (0.15f * MathF.Sin(t * MathF.Tau * 7f));
        var value = MathF.Sin(t * MathF.Tau * 55f) * 0.20f
            + MathF.Sin(t * MathF.Tau * 110f) * 0.10f
            + MathF.Sin(t * MathF.Tau * 165f) * 0.05f
            + MathF.Sin(t * MathF.Tau * 220f) * 0.03f;
        lcg = (lcg * 1664525u) + 1013904223u;
        var noise = (((lcg >> 8) & 0xFFFF) / 32768f - 1f) * 0.015f;
        value = (value * modulation) + noise;
        buffer[0][i] = value;
        buffer[1][i] = value * 0.92f;
    }

    buffer = CrossfadeLoop(buffer, SampleRate);
    Normalize(buffer, 0.6f);
    return buffer;
}

static float[][] Country()
{
    var buffer = Stereo(Samples(8.0));
    var length = buffer[0].Length;
    var state = 0x0BADF00Du;
    var filtered = 0f;
    for (var i = 0; i < length; i++)
    {
        var t = i / (float)SampleRate;
        state = (state * 1664525u) + 1013904223u;
        var white = ((state >> 8) & 0xFFFF) / 32768f - 1f;
        filtered += (white - filtered) * 0.006f;
        var breeze = 0.05f * (0.6f + (0.4f * MathF.Sin(t * MathF.Tau * 0.12f)));
        buffer[0][i] = filtered * breeze;
        buffer[1][i] = filtered * breeze * 0.9f;
    }

    // Two short original bird calls, panned apart.
    for (var call = 0; call < 2; call++)
    {
        var start = 1.4 + (call * 3.1);
        for (var note = 0; note < 3; note++)
        {
            var first = Samples(start + (note * 0.09));
            var chirpLength = Samples(0.07);
            var phase = 0f;
            for (var i = 0; i < chirpLength; i++)
            {
                var t = i / (float)chirpLength;
                var frequency = 2400f + (700f * t) + (note * 120f);
                phase += frequency / SampleRate;
                var value = MathF.Sin(phase * MathF.Tau) * 0.06f * MathF.Sin(t * MathF.PI);
                Add(buffer, first + i, value, call == 0 ? -0.35f : 0.4f);
            }
        }
    }

    buffer = CrossfadeLoop(buffer, SampleRate);
    Normalize(buffer, 0.55f);
    return buffer;
}

static float[][] Water()
{
    var buffer = Stereo(Samples(8.0));
    var length = buffer[0].Length;
    var state = 0x00C0FFEEu;
    var low = 0f;
    var band = 0f;
    for (var i = 0; i < length; i++)
    {
        var t = i / (float)SampleRate;
        state = (state * 1664525u) + 1013904223u;
        var white = ((state >> 8) & 0xFFFF) / 32768f - 1f;
        low += (white - low) * 0.03f;
        band += (low - band) * 0.20f;
        var swell = 0.6f + (0.4f * MathF.Sin(t * MathF.Tau * 0.35f));
        var value = band * 0.55f * swell;
        buffer[0][i] = value;
        buffer[1][i] = value * 0.95f;
    }

    for (var bubble = 0; bubble < 6; bubble++)
    {
        var start = 0.7 + (bubble * 1.15);
        var first = Samples(start);
        var bubbleLength = Samples(0.10);
        var phase = 0f;
        for (var i = 0; i < bubbleLength; i++)
        {
            var t = i / (float)bubbleLength;
            phase += (220f + (140f * t)) / SampleRate;
            var value = MathF.Sin(phase * MathF.Tau) * 0.025f * MathF.Sin(t * MathF.PI);
            Add(buffer, first + i, value, (bubble % 2 == 0) ? -0.3f : 0.3f);
        }
    }

    buffer = CrossfadeLoop(buffer, SampleRate);
    Normalize(buffer, 0.55f);
    return buffer;
}

static float[][] Town()
{
    var buffer = Stereo(Samples(8.0));
    var length = buffer[0].Length;
    var state = 0x0DEADBEEu;
    var low = 0f;
    for (var i = 0; i < length; i++)
    {
        var t = i / (float)SampleRate;
        state = (state * 1664525u) + 1013904223u;
        var white = ((state >> 8) & 0xFFFF) / 32768f - 1f;
        low += (white - low) * 0.012f;
        var murmur = low * 0.55f;
        var distant = MathF.Sin(t * MathF.Tau * 92f) * 0.008f * (0.5f + (0.5f * MathF.Sin(t * MathF.Tau * 0.2f)));
        buffer[0][i] = murmur + distant;
        buffer[1][i] = (murmur * 0.95f) + (distant * 0.8f);
    }

    // A few soft footsteps and a bicycle bell, all original.
    for (var step = 0; step < 5; step++)
    {
        NoiseNote(buffer, 0.6 + (step * 1.4), 0.05, 0.025f, step % 2 == 0 ? -0.4f : 0.2f, 100 + step, 0.08f);
    }

    for (var bell = 0; bell < 2; bell++)
    {
        var start = 2.2 + (bell * 3.7);
        Note(buffer, start, 0.35, 93, 0.035f, Sine, 0.45f, attack: 0.005, release: 0.30);
        Note(buffer, start, 0.30, 100, 0.020f, Sine, 0.45f, attack: 0.005, release: 0.25);
    }

    buffer = CrossfadeLoop(buffer, SampleRate);
    Normalize(buffer, 0.5f);
    return buffer;
}

static float[][] OpenRoad()
{
    var buffer = Stereo(Samples(63.0));
    var barSeconds = 60.0 / 92.0 * 4.0;
    int[][] chords =
    [
        [60, 64, 67, 72],
        [55, 59, 62, 67],
        [57, 60, 64, 69],
        [53, 57, 60, 65],
    ];
    var arpeggio = new[] { 0, 1, 2, 3, 2, 1, 2, 3 };
    for (var bar = 0; bar < 24; bar++)
    {
        var start = bar * barSeconds;
        var chord = chords[bar % chords.Length];
        Note(buffer, start, barSeconds * 0.98, chord[0] - 24, 0.15f, Triangle, 0f, attack: 0.02, release: 0.3);
        for (var voice = 0; voice < chord.Length; voice++)
        {
            var pan = (voice - 1.5f) * 0.18f;
            Note(buffer, start, barSeconds * 0.98, chord[voice], 0.055f, Sine, pan, attack: 0.25, release: 0.5);
        }

        for (var step = 0; step < 8; step++)
        {
            var note = chord[arpeggio[step]] + 12;
            var pan = step % 2 == 0 ? -0.25f : 0.25f;
            Note(buffer, start + (step * barSeconds / 8.0), barSeconds / 8.0 * 0.8, note, 0.045f, Triangle, pan, attack: 0.005, release: 0.08);
        }

        if (bar >= 2)
        {
            Kick(buffer, start, 0.16f);
            Kick(buffer, start + (barSeconds / 2.0), 0.12f);
        }

        for (var hat = 0; hat < 8; hat++)
        {
            Hat(buffer, start + (hat * barSeconds / 8.0), hat % 2 == 0 ? 0.035f : 0.018f);
        }
    }

    Fade(buffer, 2.0, 3.0);
    Normalize(buffer, 0.75f);
    return buffer;
}

static float[][] Daybreak()
{
    var buffer = Stereo(Samples(63.0));
    var barSeconds = 60.0 / 76.0 * 4.0;
    int[][] chords =
    [
        [57, 60, 64, 69],
        [53, 57, 60, 65],
        [48, 52, 55, 60],
        [55, 59, 62, 67],
    ];
    for (var bar = 0; bar < 20; bar++)
    {
        var start = bar * barSeconds;
        var chord = chords[bar % chords.Length];
        Note(buffer, start, barSeconds * 0.99, chord[0] - 12, 0.10f, Sine, 0f, attack: 0.02, release: 0.4);
        for (var voice = 0; voice < chord.Length; voice++)
        {
            Note(buffer, start, barSeconds * 0.99, chord[voice] + 12, 0.035f, Sine, (voice - 1.5f) * 0.2f, attack: 0.35, release: 0.6);
        }

        for (var step = 0; step < 4; step++)
        {
            var note = chord[step % chord.Length] + 12;
            Note(buffer, start + (step * barSeconds / 4.0), 0.8, note, 0.05f, Triangle, step % 2 == 0 ? -0.2f : 0.2f, attack: 0.01, release: 0.6);
        }
    }

    Fade(buffer, 3.0, 4.0);
    Normalize(buffer, 0.7f);
    return buffer;
}

static float[][] CrossfadeLoop(float[][] buffer, int sampleRate)
{
    var length = buffer[0].Length;
    var fade = Math.Min(sampleRate, length / 4);
    var loopLength = length - fade;
    var result = new float[buffer.Length][];
    for (var channel = 0; channel < buffer.Length; channel++)
    {
        var source = buffer[channel];
        var output = new float[loopLength];
        Array.Copy(source, output, loopLength);
        for (var i = 0; i < fade; i++)
        {
            var t = i / (float)fade;
            output[i] = (source[i] * t) + (source[loopLength + i] * (1f - t));
        }

        result[channel] = output;
    }

    return result;
}

static void Fade(float[][] buffer, double fadeIn, double fadeOut)
{
    var inSamples = Samples(fadeIn);
    var outSamples = Samples(fadeOut);
    var length = buffer[0].Length;
    for (var channel = 0; channel < buffer.Length; channel++)
    {
        for (var i = 0; i < inSamples && i < length; i++)
        {
            buffer[channel][i] *= i / (float)inSamples;
        }

        for (var i = 0; i < outSamples && i < length; i++)
        {
            buffer[channel][length - 1 - i] *= i / (float)outSamples;
        }
    }
}

static void Normalize(float[][] buffer, float peak)
{
    var maximum = 0f;
    foreach (var channel in buffer)
    {
        foreach (var sample in channel)
        {
            maximum = MathF.Max(maximum, MathF.Abs(sample));
        }
    }

    if (maximum <= 0.0001f)
    {
        return;
    }

    var scale = peak / maximum;
    foreach (var channel in buffer)
    {
        for (var i = 0; i < channel.Length; i++)
        {
            channel[i] *= scale;
        }
    }
}

static float Sine(float t, float phase) => MathF.Sin(phase * MathF.Tau);
static float Triangle(float t, float phase)
{
    var p = phase % 1f;
    return 1f - (4f * MathF.Abs(p - 0.5f));
}

static void WriteWav(string path, float[][] channels)
{
    var samples = channels[0].Length;
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write("RIFF"u8);
    writer.Write(36 + (samples * channels.Length * 2));
    writer.Write("WAVE"u8);
    writer.Write("fmt "u8);
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)channels.Length);
    writer.Write(SampleRate);
    writer.Write(SampleRate * channels.Length * 2);
    writer.Write((short)(channels.Length * 2));
    writer.Write((short)16);
    writer.Write("data"u8);
    writer.Write(samples * channels.Length * 2);
    for (var i = 0; i < samples; i++)
    {
        for (var channel = 0; channel < channels.Length; channel++)
        {
            writer.Write((short)(Math.Clamp(channels[channel][i], -1f, 1f) * short.MaxValue));
        }
    }
}

static void WriteFlac(string path, float[][] channels)
{
    var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");
    try
    {
        WriteWav(temporary, channels);
        FlacEncoder.Encode(temporary, path);
    }
    finally
    {
        File.Delete(temporary);
    }
}

delegate float Wave(float t, float phase);
