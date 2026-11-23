// Instanced boxes (feature 006): trees (opaque) and the construction preview (translucent).
// One unit cube per instance, scaled to the instance's box.

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
    // Per instance: box corners and linear colour with alpha.
    @location(2) box_min: vec4<f32>,
    @location(3) box_max: vec4<f32>,
    @location(4) color: vec4<f32>,
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) world_position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
};

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    let size = in.box_max.xyz - in.box_min.xyz;
    let local = vec3<f32>(in.position.x + 0.5, in.position.y, in.position.z + 0.5);
    let world = in.box_min.xyz + local * size;
    var out: VertexOut;
    out.clip_position = u.view_proj * vec4<f32>(world, 1.0);
    out.world_position = world;
    out.normal = in.normal;
    out.color = in.color;
    return out;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    let diffuse = max(dot(in.normal, -u.light_dir.xyz), 0.0);
    let sky = 0.5 + 0.5 * in.normal.y;
    let lit = in.color.rgb * (0.30 + 0.25 * sky + 0.65 * diffuse);

    let distance = length(in.world_position - u.camera_pos.xyz);
    let fog = clamp((distance - u.camera_pos.w * 0.5) / u.camera_pos.w, 0.0, 1.0);
    return vec4<f32>(mix(lit, u.sky_color.rgb, fog * fog), in.color.a);
}
