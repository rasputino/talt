// Voxel terrain shader: packed region-relative vertices (Talt.Core.Meshing.PackedVertex), material
// colours from a palette, per-voxel colour variation, interpolated ambient occlusion, one directional
// light, and distance fog towards the sky colour.

struct Uniforms {
    view_proj: mat4x4<f32>,
    light_dir: vec4<f32>,   // xyz: direction the light travels (normalized)
    camera_pos: vec4<f32>,  // xyz: camera position, w: fog distance
    sky_color: vec4<f32>,
    palette: array<vec4<f32>, 8>, // Linear base colour by VoxelMaterial value.
};

@group(0) @binding(0) var<uniform> u: Uniforms;

struct VertexIn {
    // x: x | y << 8 | z << 16 | face << 24; y: material | ao << 8.
    @location(0) packed: vec2<u32>,
    // Per region (instance): world position of the region origin.
    @location(1) region_origin: vec4<f32>,
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) world_position: vec3<f32>,
    @location(1) @interpolate(flat) normal: vec3<f32>,
    @location(2) @interpolate(flat) base_color: vec3<f32>,
    @location(3) ao_brightness: f32,
};

// Same order as Talt.Core.Meshing.FaceDirections: +X, -X, +Y, -Y, +Z, -Z.
fn face_normal(face: u32) -> vec3<f32> {
    switch face {
        case 0u: { return vec3<f32>(1.0, 0.0, 0.0); }
        case 1u: { return vec3<f32>(-1.0, 0.0, 0.0); }
        case 2u: { return vec3<f32>(0.0, 1.0, 0.0); }
        case 3u: { return vec3<f32>(0.0, -1.0, 0.0); }
        case 4u: { return vec3<f32>(0.0, 0.0, 1.0); }
        default: { return vec3<f32>(0.0, 0.0, -1.0); }
    }
}

// Same values as FaceDirections.AoBrightness.
fn ao_brightness(level: u32) -> f32 {
    switch level {
        case 0u: { return 0.55; }
        case 1u: { return 0.72; }
        case 2u: { return 0.86; }
        default: { return 1.0; }
    }
}

// Same hash as Talt.Core.Terrain.ValueNoise.Hash; u32 arithmetic wraps like C# unchecked uint.
fn hash(x: i32, y: i32, seed: u32) -> u32 {
    var h = seed ^ (bitcast<u32>(x) * 0x27d4eb2du) ^ (bitcast<u32>(y) * 0x165667b1u);
    h ^= h >> 15u;
    h *= 0x85ebca6bu;
    h ^= h >> 13u;
    h *= 0xc2b2ae35u;
    h ^= h >> 16u;
    return h;
}

// Same as MaterialPalette.Variation: ±5 % brightness per voxel.
fn variation(voxel: vec3<i32>) -> f32 {
    let h = hash(voxel.x * 31 + voxel.y, voxel.z, 0x9e3779b9u);
    return 0.95 + f32(h & 0xffu) / 255.0 * 0.10;
}

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    let local = vec3<f32>(
        f32(in.packed.x & 0xffu),
        f32((in.packed.x >> 8u) & 0xffu),
        f32((in.packed.x >> 16u) & 0xffu));
    let position = in.region_origin.xyz + local;
    let material = in.packed.y & 0xffu;

    var out: VertexOut;
    out.clip_position = u.view_proj * vec4<f32>(position, 1.0);
    out.world_position = position;
    out.normal = face_normal((in.packed.x >> 24u) & 0x7u);
    out.base_color = u.palette[material & 7u].rgb;
    out.ao_brightness = ao_brightness((in.packed.y >> 8u) & 0x3u);
    return out;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    // The voxel this fragment belongs to: half a voxel behind the face, along its normal.
    let voxel = vec3<i32>(floor(in.world_position - in.normal * 0.5));
    let color = in.base_color * variation(voxel) * in.ao_brightness;

    let diffuse = max(dot(in.normal, -u.light_dir.xyz), 0.0);
    // Sky light from above keeps shadowed sides readable.
    let sky = 0.5 + 0.5 * in.normal.y;
    let lighting = 0.30 + 0.25 * sky + 0.65 * diffuse;
    let lit = color * lighting;

    let distance = length(in.world_position - u.camera_pos.xyz);
    let fog = clamp((distance - u.camera_pos.w * 0.5) / u.camera_pos.w, 0.0, 1.0);
    return vec4<f32>(mix(lit, u.sky_color.rgb, fog * fog), 1.0);
}
