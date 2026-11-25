// Shared lighting for every world shader (feature 018, research R2): the uniform block, the sun with
// cascaded shadows, a sky/ground ambient term scaled by ambient occlusion, distance fog and tone mapping.
// Colours are linear until tone_map; the sRGB surface does the gamma encoding. Inserted by the
// include line of each shader (WebGpuShaders.ExpandIncludes). Must match WebGpuRenderer.Uniforms.

struct Uniforms {
    view_proj: mat4x4<f32>,      // Camera (world pass) or cascade light matrix (shadow pass).
    sun_dir: vec4<f32>,          // xyz: direction the light travels (normalized); w: time of day night factor 0–1
    sun_color: vec4<f32>,        // rgb: sun colour × intensity (linear); w: exposure
    sky_ambient: vec4<f32>,      // rgb: ambient from above; w: ambient occlusion strength 0–1
    ground_ambient: vec4<f32>,   // rgb: ambient from below; w: real time in seconds (wraps every hour)
    camera_pos: vec4<f32>,       // xyz: camera position; w: fog distance
    camera_forward: vec4<f32>,   // xyz: view direction; w: water effects on (1) or off (0)
    sky_color: vec4<f32>,        // rgb: fog and sky colour (linear); w: unused
    shadow: vec4<f32>,           // x: cascade count (0 = shadows off); y: shadow distance; z: 1 / map size; w: filter taps
    cascade_splits: vec4<f32>,   // far view depth of cascades 0–2; w: 1 / water texture size along x
    cascade_texel: vec4<f32>,    // world units per shadow texel of cascades 0–2; w: 1 / water texture size along z
    cascades: array<mat4x4<f32>, 3>,
    palette: array<vec4<f32>, 8>, // Linear base colour by VoxelMaterial value.
};

@group(0) @binding(0) var<uniform> u: Uniforms;

@group(1) @binding(0) var shadow_map: texture_depth_2d_array;
@group(1) @binding(1) var shadow_sampler: sampler_comparison;
@group(1) @binding(2) var water_map: texture_2d<f32>;
@group(1) @binding(3) var linear_sampler: sampler;
@group(1) @binding(4) var model_palette: texture_2d<f32>;

// Narkowicz's fit of the ACES filmic curve: keeps saturated voxel colours and soft highlights.
fn tone_map(color: vec3<f32>) -> vec3<f32> {
    let x = color * u.sun_color.w;
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), vec3<f32>(0.0), vec3<f32>(1.0));
}

fn apply_fog(color: vec3<f32>, world_pos: vec3<f32>) -> vec3<f32> {
    let distance = length(world_pos - u.camera_pos.xyz);
    let fog = clamp((distance - u.camera_pos.w * 0.5) / u.camera_pos.w, 0.0, 1.0);
    return mix(color, u.sky_color.rgb, fog * fog);
}

// Fraction of sunlight reaching a point (1 = lit). Cascades are chosen by view depth; lookups are offset
// along the normal by about one texel against acne on axis-aligned voxel faces; 1, 4 or 9 filtered taps.
fn shadow_factor(world_pos: vec3<f32>, normal: vec3<f32>) -> f32 {
    let count = i32(u.shadow.x + 0.5);
    if (count == 0) {
        return 1.0;
    }

    let depth = dot(world_pos - u.camera_pos.xyz, u.camera_forward.xyz);
    if (depth > u.shadow.y) {
        return 1.0;
    }

    var cascade = 0;
    if (depth > u.cascade_splits.x && count > 1) {
        cascade = 1;
    }
    if (depth > u.cascade_splits.y && count > 2) {
        cascade = 2;
    }

    let texel = u.cascade_texel[cascade];
    let p = world_pos + normal * texel * 1.5;
    let clip = u.cascades[cascade] * vec4<f32>(p, 1.0);
    let uv = clip.xy * vec2<f32>(0.5, -0.5) + vec2<f32>(0.5);
    if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0 || clip.z >= 1.0) {
        return 1.0;
    }

    let size = vec2<f32>(textureDimensions(shadow_map));
    let step = 1.0 / size;
    let reference = clip.z - 0.0005;
    let taps = i32(u.shadow.w + 0.5);
    var lit = 0.0;
    if (taps >= 9) {
        for (var y = -1; y <= 1; y++) {
            for (var x = -1; x <= 1; x++) {
                lit += textureSampleCompareLevel(shadow_map, shadow_sampler, uv + vec2<f32>(f32(x), f32(y)) * step * 1.25, cascade, reference);
            }
        }
        lit = lit / 9.0;
    } else if (taps >= 4) {
        lit += textureSampleCompareLevel(shadow_map, shadow_sampler, uv + vec2<f32>(-0.6, -0.6) * step, cascade, reference);
        lit += textureSampleCompareLevel(shadow_map, shadow_sampler, uv + vec2<f32>(0.6, -0.6) * step, cascade, reference);
        lit += textureSampleCompareLevel(shadow_map, shadow_sampler, uv + vec2<f32>(-0.6, 0.6) * step, cascade, reference);
        lit += textureSampleCompareLevel(shadow_map, shadow_sampler, uv + vec2<f32>(0.6, 0.6) * step, cascade, reference);
        lit = lit * 0.25;
    } else {
        lit = textureSampleCompareLevel(shadow_map, shadow_sampler, uv, cascade, reference);
    }

    // Fade out over the last tenth of the shadow distance instead of ending in a line.
    let fade = smoothstep(u.shadow.y * 0.9, u.shadow.y, depth);
    return mix(lit, 1.0, fade);
}

// Lit colour (before fog and tone mapping) of a surface with albedo, normal and ambient occlusion
// brightness (1 = open, lower in corners).
fn shade(albedo: vec3<f32>, normal: vec3<f32>, world_pos: vec3<f32>, ao_brightness: f32) -> vec3<f32> {
    let ao = mix(1.0, ao_brightness, u.sky_ambient.w);
    let ambient = mix(u.ground_ambient.rgb, u.sky_ambient.rgb, 0.5 + 0.5 * normal.y) * ao;
    let facing = dot(normal, -u.sun_dir.xyz);
    var sun = vec3<f32>(0.0);
    if (facing > 0.0) {
        sun = u.sun_color.rgb * facing * shadow_factor(world_pos, normal) * mix(1.0, ao, 0.35);
    }

    return albedo * (ambient + sun);
}

// Shade, fog and tone map in one call (most shaders).
fn finish(albedo: vec3<f32>, normal: vec3<f32>, world_pos: vec3<f32>, ao_brightness: f32) -> vec3<f32> {
    return tone_map(apply_fog(shade(albedo, normal, world_pos, ao_brightness), world_pos));
}
