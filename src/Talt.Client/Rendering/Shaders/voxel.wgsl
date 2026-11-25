// Voxel terrain shader: packed region-relative vertices (Talt.Core.Meshing.PackedVertex), material
// colours from a palette, per-voxel colour variation, interpolated ambient occlusion, the shared sun,
// sky and shadow lighting (lighting.wgsl), and animated water (feature 018, research R5).

// #include "lighting.wgsl"

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
    @location(4) @interpolate(flat) material: u32,
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

// Softer than FaceDirections.AoBrightness (feature 003): corners read clearly without black creases.
fn ao_brightness(level: u32) -> f32 {
    switch level {
        case 0u: { return 0.50; }
        case 1u: { return 0.68; }
        case 2u: { return 0.85; }
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

// Smooth value noise on a 2-D lattice, 0–1.
fn value_noise(p: vec2<f32>) -> f32 {
    let i = vec2<i32>(floor(p));
    let f = fract(p);
    let s = f * f * (3.0 - 2.0 * f);
    let a = f32(hash(i.x, i.y, 0x51ed27u) & 0xffffu) / 65535.0;
    let b = f32(hash(i.x + 1, i.y, 0x51ed27u) & 0xffffu) / 65535.0;
    let c = f32(hash(i.x, i.y + 1, 0x51ed27u) & 0xffffu) / 65535.0;
    let d = f32(hash(i.x + 1, i.y + 1, 0x51ed27u) & 0xffffu) / 65535.0;
    return mix(mix(a, b, s.x), mix(c, d, s.x), s.y);
}

// Height of the animated water surface pattern (only its slope is used).
fn wave(p: vec2<f32>, t: f32) -> f32 {
    return value_noise(p * 0.9 + vec2<f32>(t * 0.35, t * 0.22)) * 0.6
        + value_noise(p * 2.3 - vec2<f32>(t * 0.27, -t * 0.41)) * 0.3
        + sin(p.x * 1.7 + p.y * 1.1 + t * 1.3) * 0.05;
}

fn shade_water(in: VertexOut) -> vec3<f32> {
    let t = u.ground_ambient.w;
    let p = in.world_position.xz;
    let e = 0.15;
    let h = wave(p, t);
    let slope = vec2<f32>(wave(p + vec2<f32>(e, 0.0), t) - h, wave(p + vec2<f32>(0.0, e), t) - h) / e;
    let n = normalize(vec3<f32>(-slope.x * 0.12, 1.0, -slope.y * 0.12));

    // Distance to land (r, 0–4 tiles) and depth (g, 0–8 voxels) per tile, filtered so shores are smooth.
    let uv = p * vec2<f32>(u.cascade_splits.w, u.cascade_texel.w);
    let water = textureSampleLevel(water_map, linear_sampler, uv, 0.0).rg;
    let shallow = vec3<f32>(0.10, 0.42, 0.52);
    let deep = vec3<f32>(0.015, 0.10, 0.26);
    let albedo = mix(shallow, deep, smoothstep(0.0, 0.8, water.g));

    let view = normalize(u.camera_pos.xyz - in.world_position);
    let lit = shade(albedo, n, in.world_position, mix(1.0, in.ao_brightness, 0.5));
    let shadow = shadow_factor(in.world_position, vec3<f32>(0.0, 1.0, 0.0));
    let fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(n, view), 0.0), 5.0);
    var color = mix(lit, u.sky_color.rgb * 0.85, fresnel * 0.55);
    let reflected = reflect(u.sun_dir.xyz, n);
    color += u.sun_color.rgb * pow(max(dot(reflected, view), 0.0), 90.0) * 0.8 * shadow;

    // Shore: the shallow edge takes the colour of the nearest land tile (stored per texel), darkened as if
    // wet, and fades into the water; a thin, faint foam line breaks a little further out.
    let texel = vec2<i32>(clamp(uv, vec2<f32>(0.0), vec2<f32>(0.9999)) * vec2<f32>(textureDimensions(water_map)));
    let facts = textureLoad(water_map, texel, 0);
    let land_material = u32(facts.b * 255.0 + 0.5);
    let wet = shade(u.palette[land_material & 7u].rgb * 0.72, vec3<f32>(0.0, 1.0, 0.0), in.world_position, 1.0);
    let breakup = value_noise(p * 3.0 + vec2<f32>(t * 0.2, t * 0.15));
    let shore = 1.0 - smoothstep(0.125, 0.30 + (breakup - 0.5) * 0.06, water.r);
    color = mix(color, wet, shore * 0.85 * step(0.001, water.r));
    let line = 0.24 + sin(t * 1.2) * 0.012 + (breakup - 0.5) * 0.04;
    let foam = (1.0 - smoothstep(0.0, 0.025, abs(water.r - line))) * smoothstep(0.35, 0.6, breakup) * facts.a; // No foam line on rivers.
    let foam_color = shade(vec3<f32>(0.80, 0.86, 0.88), vec3<f32>(0.0, 1.0, 0.0), in.world_position, 1.0);
    return mix(color, foam_color, foam * 0.35);
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
    out.material = material;
    return out;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    var lit: vec3<f32>;
    if (in.material == 5u && in.normal.y > 0.5 && u.camera_forward.w > 0.5) {
        lit = shade_water(in);
    } else {
        // The voxel this fragment belongs to: half a voxel behind the face, along its normal.
        let voxel = vec3<i32>(floor(in.world_position - in.normal * 0.5));
        lit = shade(in.base_color * variation(voxel), in.normal, in.world_position, in.ao_brightness);
    }

    return vec4<f32>(tone_map(apply_fog(lit, in.world_position)), 1.0);
}
