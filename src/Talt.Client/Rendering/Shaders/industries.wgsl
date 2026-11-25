// Industries: one coloured box per model box, instanced (feature 009; final art in 018).

// #include "lighting.wgsl"

struct VertexIn {
    // Per vertex: unit cube centered on X/Z, from 0 to 1 on Y.
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    // Per instance.
    @location(2) instance_position: vec4<f32>, // xyz: centre of the base
    @location(3) instance_extent: vec4<f32>,   // xyz: width, height, depth
    @location(4) instance_color: vec4<f32>,
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) world_position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec3<f32>,
};

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    let world = in.instance_position.xyz + in.position * in.instance_extent.xyz;
    var out: VertexOut;
    out.clip_position = u.view_proj * vec4<f32>(world, 1.0);
    out.world_position = world;
    out.normal = in.normal;
    // Slightly darker lower part suggests walls with a roof.
    out.color = in.instance_color.rgb * select(0.82, 1.0, in.position.y > 0.35);
    return out;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    return vec4<f32>(finish(in.color, in.normal, in.world_position, 1.0), 1.0);
}
