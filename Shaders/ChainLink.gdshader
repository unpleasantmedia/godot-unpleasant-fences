// A chain-link fence's mesh: wire in a diamond pattern, the gaps between cut away. The pattern is
// laid out in the world (along the fence, and up), not by each piece's own UVs, so it runs on
// unbroken from one metre of fence to the next, however much each is stretched to fit its posts.
shader_type spatial;
render_mode cull_disabled;

uniform vec3 wire_color : source_color = vec3(0.62, 0.64, 0.66);
// How far apart the wires are, and how thick, in metres.
uniform float spacing = 0.06;
uniform float thickness = 0.004;

varying vec3 world_position;
varying vec3 world_normal;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

void fragment() {
	// Along the fence (level, square to its face) and up.
	vec2 across = normalize(world_normal.xz + vec2(1e-6));
	vec2 uv = vec2(dot(world_position.xz, vec2(-across.y, across.x)), world_position.y);
	// Turned 45 degrees: the wires run diagonally both ways, one every spacing metres across.
	vec2 d = vec2(uv.x + uv.y, uv.x - uv.y) / (spacing * 1.41421);
	vec2 off = (0.5 - abs(fract(d) - 0.5)) * spacing;
	float edge = min(off.x, off.y);
	// The wire's width on screen held to at least about a pixel, so it does not break up far off.
	float wide = max(thickness * 0.5, fwidth(edge) * 0.75);
	if (edge > wide) {
		discard;
	}
	ALBEDO = wire_color;
	METALLIC = 0.8;
	ROUGHNESS = 0.45;
}
