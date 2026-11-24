// Road meshes (feature 007, research R10): road pieces, bridges and tunnel portals on top of the terrain.
// Non-indexed triangles in world space with a per-vertex normal and linear colour; lit like the boxes.

struct Uniforms {
    view_proj: mat4x4<f32>,
    light_dir: vec4<f32>,
    camera_pos: vec4<f32>,
    sky_color: vec4<f32>,
};

@group(0) @binding(0) var<uniform> u: Uniforms;

struct VertexIn {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) world_position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
};

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    out.clip_position = u.view_proj * vec4<f32>(in.position, 1.0);
    out.world_position = in.position;
    out.normal = in.normal;
    out.color = in.color;
    return out;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    let n = normalize(in.normal);
    let diffuse = max(dot(n, -u.light_dir.xyz), 0.0);
    let sky = 0.5 + 0.5 * n.y;
    let lit = in.color.rgb * (0.30 + 0.25 * sky + 0.65 * diffuse);

    let distance = length(in.world_position - u.camera_pos.xyz);
    let fog = clamp((distance - u.camera_pos.w * 0.5) / u.camera_pos.w, 0.0, 1.0);
    return vec4<f32>(mix(lit, u.sky_color.rgb, fog * fog), 1.0);
}
