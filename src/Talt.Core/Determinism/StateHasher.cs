using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace Talt.Core.Determinism;

/// <summary>
/// Something that contributes its state to the simulation fingerprint.
/// </summary>
public interface IStateHashable
{
    /// <summary>Appends all simulation state, in a fixed order.</summary>
    void AppendTo(StateHasher hasher);
}

/// <summary>
/// Builds the 64-bit fingerprint of the simulation state with XxHash3 (research R9).
/// Equal states give equal fingerprints on every supported platform.
/// </summary>
public sealed class StateHasher
{
    private readonly XxHash3 _hash = new();

    public StateHasher()
    {
        // Raw array bytes are hashed directly; all supported targets are little-endian.
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("State fingerprints require a little-endian platform.");
        }
    }

    public void Append(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _hash.Append(bytes);
    }

    public void Append(long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        _hash.Append(bytes);
    }

    public void Append(ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _hash.Append(bytes);
    }

    /// <summary>Appends the raw bytes of a span of unmanaged values (only the given slice).</summary>
    public void Append<T>(ReadOnlySpan<T> values)
        where T : unmanaged
    {
        Append(values.Length);
        _hash.Append(MemoryMarshal.AsBytes(values));
    }

    /// <summary>Appends a string as UTF-8 (content ids and keys; not used in per-tick state).</summary>
    public void Append(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _hash.Append(System.Text.Encoding.UTF8.GetBytes(value));
    }

    public void Append(IStateHashable part)
    {
        ArgumentNullException.ThrowIfNull(part);
        part.AppendTo(this);
    }

    public ulong GetFingerprint() => _hash.GetCurrentHashAsUInt64();

    /// <summary>Formats a fingerprint as 16 uppercase hex digits (the recorded-session format).</summary>
    public static string Format(ulong fingerprint) => fingerprint.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Parses a fingerprint written by <see cref="Format"/>.</summary>
    public static ulong Parse(string text) =>
        ulong.Parse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
}
