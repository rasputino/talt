using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace Talt.Client.Rendering;

/// <summary>
/// wgpu-native extensions (wgpu.h) that the Silk.NET core bindings do not expose: submitting with a
/// submission index and waiting for a given submission. Used to limit how many frames the CPU can run
/// ahead of the GPU.
/// </summary>
internal static unsafe partial class NativeWgpu
{
    private const string Library = "wgpu_native";

    /// <summary>Like <c>wgpuQueueSubmit</c>, returning the submission index.</summary>
    [LibraryImport(Library, EntryPoint = "wgpuQueueSubmitForIndex")]
    public static partial ulong QueueSubmitForIndex(Queue* queue, nuint commandCount, CommandBuffer** commands);

    /// <summary>Processes finished GPU work; with <paramref name="wait"/> = 1 blocks until the given submission is done. Returns 1 when the queue is empty.</summary>
    [LibraryImport(Library, EntryPoint = "wgpuDevicePoll")]
    public static partial uint DevicePoll(Device* device, uint wait, WrappedSubmissionIndex* submissionIndex);

    /// <summary>Layout of <c>WGPUWrappedSubmissionIndex</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WrappedSubmissionIndex
    {
        public Queue* Queue;
        public ulong SubmissionIndex;
    }
}
