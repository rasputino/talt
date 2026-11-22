// Vehicles: one unit cube per instance, stretched along the direction of travel.

struct Uniforms {
    view_proj: mat4x4<f32>,
    light_dir: vec4<f32>,
    camera_pos: vec4<f32>,
    sky_color: vec4<f32>,
};

@group(0) @binding(0) var<uniform> u: Uniforms;

struct VertexIn {
    // Per vertex: unit cube centered on X/Z, from 0 to 1 on Y.
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    // Per instance.
    @location(2) instance_position: vec4<f32>, // xyz: base center, w: 0 = moving along X, 1 = along Z
    @location(3) instance_color: vec4<f32>,
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) world_position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec3<f32>,
};

const VEHICLE_SIZE = vec3<f32>(0.9, 0.6, 0.42); // length, height, width

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    var size = VEHICLE_SIZE;
    if (in.instance_position.w > 0.5) {
        size = VEHICLE_SIZE.zyx;
    }

    let world = in.instance_position.xyz + in.position * size;
    var out: VertexOut;
    out.clip_position = u.view_proj * vec4<f32>(world, 1.0);
    out.world_position = world;
    out.normal = in.normal;
    // Slightly darker bottom half suggests windows on top and a body below.
    out.color = in.instance_color.rgb * select(0.8, 1.0, in.position.y > 0.5);
    return out;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    let diffuse = max(dot(in.normal, -u.light_dir.xyz), 0.0);
    let sky = 0.5 + 0.5 * in.normal.y;
    let lit = in.color * (0.30 + 0.25 * sky + 0.65 * diffuse);

    let distance = length(in.world_position - u.camera_pos.xyz);
    let fog = clamp((distance - u.camera_pos.w * 0.5) / u.camera_pos.w, 0.0, 1.0);
    return vec4<f32>(mix(lit, u.sky_color.rgb, fog * fog), 1.0);
}
