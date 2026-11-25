// Darkens the frame toward its corners, wider on the long axis.
vec4 CxStage(vec4 c)
{
    vec2 d = (vUV - 0.5) * vec2(1.0, 0.8);
    float r = length(d) / max(uSize, 0.001);
    float v = 1.0 - uStrength * smoothstep(0.4, 1.0, r);
    return vec4(c.rgb * v, c.a);
}
