// Town buildings (feature 008, research R12): one stretched unit cube per box, instanced. Wall boxes draw their
// windows procedurally from the world position (one row per storey); roof boxes are plain. Final art in 018.

// #include "lighting.wgsl"

struct VertexIn {
    // Per vertex: unit cube centred on X/Z, from 0 to 1 on Y.
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    // Per instance.
    @location(2) instance_position: vec4<f32>, // xyz: centre of the base; w: window style
    @location(3) instance_extent: vec4<f32>,   // xyz: size; w: storey height
    @location(4) instance_color: vec4<f32>,    // rgb; w: per-building variation
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) world_position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
    // x: window style, y: storey height, z: base height, w: wall height.
    @location(3) params: vec4<f32>,
    @location(4) local: vec2<f32>,
};

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    let world = in.instance_position.xyz + in.position * in.instance_extent.xyz;
    var out: VertexOut;
    out.clip_position = u.view_proj * vec4<f32>(world, 1.0);
    out.world_position = world;
    out.normal = in.normal;
    out.color = in.instance_color;
    out.params = vec4<f32>(in.instance_position.w, in.instance_extent.w, in.instance_position.y, in.instance_extent.y);
    // Horizontal position along the face, in world units from the box corner.
    out.local = vec2<f32>((in.position.x + 0.5) * in.instance_extent.x, (in.position.z + 0.5) * in.instance_extent.z);
    return out;
}

fn window_mask(along: f32, height: f32, storey: f32, style: f32, wall_height: f32) -> f32 {
    if (style < 0.5 || storey <= 0.0) {
        return 0.0;
    }

    // Leave the ground-floor band and the top edge plain.
    if (height < storey * 0.35 || height > wall_height - storey * 0.2) {
        return 0.0;
    }

    let row = fract(height / storey);
    let in_row = step(0.30, row) * step(row, 0.78);
    // Rows: two windows per tile; grid: three narrower ones.
    let per_tile = select(2.0, 3.0, style > 1.5);
    let column = fract(along * per_tile);
    let in_column = step(0.22, column) * step(column, 0.72);
    return in_row * in_column;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    var color = in.color.rgb;
    let vertical = abs(in.normal.y) < 0.5;
    if (vertical) {
        let along = select(in.local.y, in.local.x, abs(in.normal.z) > 0.5);
        let height = in.world_position.y - in.params.z;
        let mask = window_mask(along, height, in.params.y, in.params.x, in.params.w);
        let glass = vec3<f32>(0.16, 0.20, 0.26) + vec3<f32>(0.10, 0.09, 0.04) * in.color.w;
        color = mix(color, glass, mask);
    }

    // Lit windows at night (day/night cycle): about 60 % of windows, chosen per building and window.
    var glow = vec3<f32>(0.0);
    if (vertical && u.sun_dir.w > 0.0) {
        let along = select(in.local.y, in.local.x, abs(in.normal.z) > 0.5);
        let height = in.world_position.y - in.params.z;
        let mask = window_mask(along, height, in.params.y, in.params.x, in.params.w);
        let cell = vec2<f32>(floor(along * 3.0), floor(height / max(in.params.y, 0.01)));
        let lit = fract(sin(dot(cell + in.color.w * 17.0, vec2<f32>(12.9898, 78.233))) * 43758.5453) < 0.6;
        glow = select(vec3<f32>(0.0), vec3<f32>(1.0, 0.72, 0.38) * 3.0 * mask * u.sun_dir.w, lit);
    }

    return vec4<f32>(tone_map(apply_fog(shade(color, in.normal, in.world_position, 1.0) + glow, in.world_position)), 1.0);
}
