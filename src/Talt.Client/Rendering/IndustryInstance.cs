using System.Numerics;
using System.Runtime.InteropServices;

namespace Talt.Client.Rendering;

/// <summary>
/// One box of an industry placeholder model, ready for the instanced renderer. Layout must match
/// <c>industries.wgsl</c>.
/// </summary>
/// <param name="Position">xyz: centre of the box's base; w unused.</param>
/// <param name="Extent">xyz: width, height and depth in tiles/voxels.</param>
/// <param name="Color">RGB colour (w unused).</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct IndustryInstance(Vector4 Position, Vector4 Extent, Vector4 Color);
