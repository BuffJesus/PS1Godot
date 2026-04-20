#[compute]
#version 450

// Nearest-neighbor blit between two storage images of (potentially)
// different sizes. Called twice by PS1PixelizeEffect:
//   1. viewport color → 320×240 scratch   (downsample)
//   2. 320×240 scratch → viewport color   (upsample, blocky)
// Net effect: viewport looks as if rendered at PS1 resolution.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform readonly  image2D src_image;
layout(rgba16f, set = 0, binding = 1) uniform writeonly image2D dst_image;

layout(push_constant, std430) uniform Params {
    uvec2 src_size;
    uvec2 dst_size;
} params;

void main() {
    uvec2 dst_pixel = gl_GlobalInvocationID.xy;
    if (dst_pixel.x >= params.dst_size.x || dst_pixel.y >= params.dst_size.y) return;

    // Proportional nearest lookup into src.
    uvec2 src_pixel = (dst_pixel * params.src_size) / params.dst_size;
    src_pixel = min(src_pixel, params.src_size - uvec2(1));

    vec4 col = imageLoad(src_image, ivec2(src_pixel));
    imageStore(dst_image, ivec2(dst_pixel), col);
}
