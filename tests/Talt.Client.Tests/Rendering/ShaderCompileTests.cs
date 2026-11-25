using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Talt.Client.Rendering;

namespace Talt.Client.Tests.Rendering;

/// <summary>
/// Compiles every WGSL shader through wgpu-native (feature 018). Needs a GPU adapter, so CI (no GPU) skips
/// it with the trait filter <c>Category!=Gpu</c>.
/// </summary>
[Trait("Category", "Gpu")]
public sealed unsafe class ShaderCompileTests
{
    private static readonly List<string> Errors = [];
    private static readonly ErrorCallback OnError = (type, message, _) =>
    {
        lock (Errors)
        {
            Errors.Add($"{type}: {SilkMarshal.PtrToString((nint)message)}");
        }
    };

    private static readonly PfnErrorCallback OnErrorPfn = new(OnError);

    [Fact]
    public void EveryShader_Compiles()
    {
        var wgpu = WebGPU.GetApi();
        var instanceDescriptor = new InstanceDescriptor();
        var instance = wgpu.CreateInstance(&instanceDescriptor);
        Adapter* adapter = null;
        var options = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
        wgpu.InstanceRequestAdapter(instance, &options, new PfnRequestAdapterCallback((status, a, _, _) =>
        {
            if (status == RequestAdapterStatus.Success)
            {
                adapter = a;
            }
        }), null);
        Assert.True(adapter != null, "No GPU adapter.");

        Device* device = null;
        var deviceDescriptor = new DeviceDescriptor();
        wgpu.AdapterRequestDevice(adapter, &deviceDescriptor, new PfnRequestDeviceCallback((status, d, _, _) =>
        {
            if (status == RequestDeviceStatus.Success)
            {
                device = d;
            }
        }), null);
        Assert.True(device != null, "No GPU device.");
        wgpu.DeviceSetUncapturedErrorCallback(device, OnErrorPfn, null);

        foreach (var name in new[] { "voxel.wgsl", "models.wgsl", "roads.wgsl", "towns.wgsl", "boxes.wgsl", "vehicles.wgsl", "industries.wgsl", "highlight.wgsl", "ui.wgsl" })
        {
            var module = WebGpuShaders.CreateModule(wgpu, device, WebGpuShaders.Load(name));
            Assert.True(module != null, name);
            wgpu.ShaderModuleRelease(module);
        }

        _ = NativeWgpu.DevicePoll(device, 1, null);
        lock (Errors)
        {
            Assert.Empty(Errors);
        }

        wgpu.DeviceRelease(device);
        wgpu.AdapterRelease(adapter);
        wgpu.InstanceRelease(instance);
    }
}
