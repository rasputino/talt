// Voxel models (feature 018, research R6): vertices in half-voxel units relative to the model's pivot,
// 8 model voxels per tile, rotated by the instance's yaw; colours from the model's palette row, whose
// alpha says the colour's role: opaque, paint (multiplied by the instance tint) or glow (lit at night).

// #include "lighting.wgsl"

struct VertexIn {
    @location(0) position: vec4<i32>,      // x, y, z in half voxels; w: face | ao << 3
    @location(1) color: u32,               // palette index | palette row << 8
    @location(2) instance_position: vec4<f32>, // xyz: pivot; w: yaw (radians)
    @location(3) instance_tint: vec4<f32>,     // rgb: paint tint; w: variation 0–1
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) world_position: vec3<f32>,
    @location(1) @interpolate(flat) normal: vec3<f32>,
    @location(2) @interpolate(flat) palette: vec2<u32>,
    @location(3) @interpolate(flat) tint: vec4<f32>,
    @location(4) ao_brightness: f32,
    @location(5) local_position: vec3<f32>,
    @location(6) @interpolate(flat) local_normal: vec3<f32>,
};

const VERTEX_SCALE = 1.0 / 16.0;

fn face_normal(face: i32) -> vec3<f32> {
    switch face {
        case 0: { return vec3<f32>(1.0, 0.0, 0.0); }
        case 1: { return vec3<f32>(-1.0, 0.0, 0.0); }
        case 2: { return vec3<f32>(0.0, 1.0, 0.0); }
        case 3: { return vec3<f32>(0.0, -1.0, 0.0); }
        case 4: { return vec3<f32>(0.0, 0.0, 1.0); }
        default: { return vec3<f32>(0.0, 0.0, -1.0); }
    }
}

fn ao_brightness(level: i32) -> f32 {
    switch level {
        case 0: { return 0.50; }
        case 1: { return 0.68; }
        case 2: { return 0.85; }
        default: { return 1.0; }
    }
}

fn rotate_y(v: vec3<f32>, yaw: f32) -> vec3<f32> {
    let c = cos(yaw);
    let s = sin(yaw);
    return vec3<f32>(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
}

fn hash3(p: vec3<i32>, seed: u32) -> u32 {
    var h = seed ^ (bitcast<u32>(p.x) * 0x27d4eb2du) ^ (bitcast<u32>(p.y) * 0x165667b1u) ^ (bitcast<u32>(p.z) * 0x9e3779b1u);
    h ^= h >> 15u;
    h *= 0x85ebca6bu;
    h ^= h >> 13u;
    return h;
}

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    let local = vec3<f32>(in.position.xyz) * VERTEX_SCALE;
    let yaw = in.instance_position.w;
    let world = in.instance_position.xyz + rotate_y(local, yaw);
    let face = in.position.w & 7;

    var out: VertexOut;
    out.clip_position = u.view_proj * vec4<f32>(world, 1.0);
    out.world_position = world;
    out.local_normal = face_normal(face);
    out.normal = rotate_y(out.local_normal, yaw);
    out.palette = vec2<u32>(in.color & 0xffu, in.color >> 8u);
    out.tint = in.instance_tint;
    out.ao_brightness = ao_brightness((in.position.w >> 3) & 3);
    out.local_position = local;
    return out;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    let entry = textureLoad(model_palette, vec2<i32>(i32(in.palette.x), i32(in.palette.y)), 0);
    // The model voxel of this fragment (8 per tile), half a voxel behind the face.
    let voxel = vec3<i32>(floor(in.local_position * 8.0 - in.local_normal * 0.5));
    let jitter = 0.96 + f32(hash3(voxel, 0x5bd1e995u) & 0xffu) / 255.0 * 0.08;

    var albedo = entry.rgb * jitter;
    var glow = vec3<f32>(0.0);
    if (entry.a > 0.25 && entry.a < 0.75) {
        albedo = entry.rgb * in.tint.rgb * jitter; // Paint: the vehicle's colour.
    } else if (entry.a <= 0.25) {
        // Glass: dark by day, about 60 % of windows lit at night (chosen per object and window voxel).
        let cell = vec3<i32>(voxel.x / 2, voxel.y / 3, voxel.z / 2);
        let on = f32(hash3(cell, u32(in.tint.w * 65535.0)) & 0xffu) / 255.0 < 0.6;
        if (on && u.sun_dir.w > 0.0) {
            glow = vec3<f32>(1.0, 0.70, 0.36) * 3.0 * u.sun_dir.w;
        }
    }

    let lit = shade(albedo, in.normal, in.world_position, in.ao_brightness) + glow;
    return vec4<f32>(tone_map(apply_fog(lit, in.world_position)), 1.0);
}
