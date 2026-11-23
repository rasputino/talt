// Interface: rounded rectangles (exact SDF), SDF text and pixel-art icons from one atlas (feature 005).

struct Uniforms {
    viewport: vec2<f32>,
    unused: vec2<f32>,
};

@group(0) @binding(0) var<uniform> u: Uniforms;
@group(0) @binding(1) var atlas: texture_2d<f32>;
@group(0) @binding(2) var linear_sampler: sampler;
@group(0) @binding(3) var nearest_sampler: sampler;

struct VertexIn {
    @location(0) position: vec2<f32>, // framebuffer pixels
    @location(1) uv: vec2<f32>,
    @location(2) local: vec2<f32>,    // position relative to the shape centre
    @location(3) color: vec4<f32>,    // sRGB
    @location(4) shape: vec4<f32>,    // half size, corner radius, mode (0 rect, 1 text, 2 icon)
};

struct VertexOut {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) local: vec2<f32>,
    @location(2) color: vec4<f32>,
    @location(3) shape: vec4<f32>,
};

@vertex
fn vs_main(in: VertexIn) -> VertexOut {
    var out: VertexOut;
    out.clip_position = vec4<f32>(in.position.x / u.viewport.x * 2.0 - 1.0, 1.0 - in.position.y / u.viewport.y * 2.0, 0.0, 1.0);
    out.uv = in.uv;
    out.local = in.local;
    out.color = in.color;
    out.shape = in.shape;
    return out;
}

fn to_linear(c: vec3<f32>) -> vec3<f32> {
    return select(pow((c + vec3<f32>(0.055)) / 1.055, vec3<f32>(2.4)), c / 12.92, c <= vec3<f32>(0.04045));
}

fn rounded_rect_distance(p: vec2<f32>, half_size: vec2<f32>, radius: f32) -> f32 {
    let q = abs(p) - half_size + vec2<f32>(radius);
    return length(max(q, vec2<f32>(0.0))) + min(max(q.x, q.y), 0.0) - radius;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    // Sampling and derivatives must happen in uniform control flow, so before choosing the mode.
    let smooth_sample = textureSample(atlas, linear_sampler, in.uv);
    let pixel_sample = textureSample(atlas, nearest_sampler, in.uv);
    let edge_width = max(fwidth(smooth_sample.a) * 0.7, 0.0001);

    var color = vec4<f32>(to_linear(in.color.rgb), in.color.a);
    if (in.shape.w < 0.5) {
        let d = rounded_rect_distance(in.local, in.shape.xy, in.shape.z);
        color.a = color.a * clamp(0.5 - d, 0.0, 1.0);
    } else if (in.shape.w < 1.5) {
        color.a = color.a * smoothstep(0.5 - edge_width, 0.5 + edge_width, smooth_sample.a);
    } else {
        color = vec4<f32>(to_linear(pixel_sample.rgb) * color.rgb, pixel_sample.a * color.a);
    }

    return vec4<f32>(color.rgb * color.a, color.a); // Premultiplied alpha.
}
