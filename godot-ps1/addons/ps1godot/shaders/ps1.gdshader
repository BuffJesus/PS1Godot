// PlayStation 1 spatial shader.
//
// Reproduces four hallmark PS1 artifacts:
//   1. Vertex snapping — integer pixel-grid positions cause sub-pixel jitter.
//   2. Nearest-neighbor texturing — no filtering between texels.
//   3. 2× color modulate — PSYQo vertex colors where 128 = neutral, 255 = 2×.
//   4. Per-vertex flat/Gouraud lighting — baked into COLOR, no fragment lighting.
//
// What we intentionally DON'T reproduce at the shader level:
//   - True affine texture mapping (requires the `noperspective` varying qualifier,
//     which Godot 4.x shader language doesn't expose reliably). The export
//     pipeline subdivides large triangles to limit perspective warp instead.
//
// Apply this shader's material to any MeshInstance3D, or let PS1MeshInstance
// auto-apply `ps1_default.tres`.

shader_type spatial;
// depth_draw_always (not opaque): unshaded materials in Godot 4 are sometimes
// not classified as "opaque" for depth-pass purposes, so depth_draw_opaque can
// silently skip writes — letting the floor/sky sort in front of cubes at
// certain orbit angles and making them appear to vanish. depth_draw_always
// guarantees a depth write regardless of pass classification.
render_mode unshaded, cull_back, depth_draw_always, specular_disabled;

group_uniforms texture;
uniform sampler2D albedo_tex : source_color, filter_nearest, repeat_enable, hint_default_white;
uniform vec4 tint_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

group_uniforms snap;
uniform bool snap_enabled = true;
// Match SplashEdit's default PS1 framebuffer (320×240 NTSC).
uniform vec2 snap_resolution = vec2(320.0, 240.0);

group_uniforms modulate;
// PSYQo treats vertex-color 128 as neutral, so painted 1.0 values double.
// Set to 1.0 if you bake brightness directly into COLOR instead.
uniform float modulate_scale : hint_range(0.0, 4.0) = 2.0;

group_uniforms fog;
uniform bool fog_enabled = false;
uniform vec3 fog_color : source_color = vec3(0.5, 0.5, 0.6);
uniform float fog_near : hint_range(0.0, 1000.0) = 10.0;
uniform float fog_far : hint_range(0.0, 1000.0) = 50.0;

varying vec3 v_vertex_color;
varying float v_fog;

void vertex() {
    vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    vec4 clip_pos = PROJECTION_MATRIX * view_pos;

    // Snap only when the vertex is in front of the camera. For vertices behind
    // the near plane (w <= 0), the perspective-divide / multiply round-trip
    // flips signs and produces garbage clip coords — triangles that span the
    // near plane then degenerate or vanish. Letting the GPU's normal clipping
    // handle near-plane verts is correct PS1 behavior anyway.
    if (snap_enabled && clip_pos.w > 0.0) {
        vec2 grid = snap_resolution * 0.5;
        vec2 ndc = clip_pos.xy / clip_pos.w;
        ndc = floor(ndc * grid + 0.5) / grid;
        clip_pos.xy = ndc * clip_pos.w;
    }

    POSITION = clip_pos;

    v_vertex_color = COLOR.rgb;
    float view_depth = -view_pos.z;
    v_fog = clamp((view_depth - fog_near) / max(fog_far - fog_near, 0.0001), 0.0, 1.0);
}

void fragment() {
    vec4 tex = texture(albedo_tex, UV);
    vec3 col = tex.rgb * tint_color.rgb * v_vertex_color * modulate_scale;
    if (fog_enabled) {
        col = mix(col, fog_color, v_fog);
    }
    ALBEDO = col;
    ALPHA = tex.a;
}
