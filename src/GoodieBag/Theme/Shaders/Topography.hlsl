// 3D simplex noise: Ashima Arts / Stefan Gustavson (MIT)

sampler2D input : register(s0);
float2 size : register(c0);
float unit : register(c1);
float z : register(c2);
float4 lineColor : register(c3);
float4 background : register(c4);
float haze : register(c5);
float strength : register(c6);

static const float Density = 17.0;
static const float Brightness = 0.15;

float3 m289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 m289(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 perm(float4 x) { return m289(((x * 34.0) + 1.0) * x); }

float snoise(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);
    float3 i = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g, l.zxy);
    float3 i2 = max(g, l.zxy);
    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - D.yyy;
    i = m289(i);
    float4 p = perm(perm(perm(i.z + float4(0.0, i1.z, i2.z, 1.0)) + i.y + float4(0.0, i1.y, i2.y, 1.0)) + i.x + float4(0.0, i1.x, i2.x, 1.0));
    float3 ns = 0.142857142857 * D.wyz - D.xzx;
    float4 j = p - 49.0 * floor(p * ns.z * ns.z);
    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_);
    float4 x = x_ * ns.x + ns.yyyy;
    float4 y = y_ * ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x) - abs(y);
    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);
    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, 0.0);
    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
    float3 p0 = float3(a0.xy, h.x);
    float3 p1 = float3(a0.zw, h.y);
    float3 p2 = float3(a1.xy, h.z);
    float3 p3 = float3(a1.zw, h.w);
    float4 nr = 1.79284291400159 - 0.85373472095314 * float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3));
    p0 *= nr.x; p1 *= nr.y; p2 *= nr.z; p3 *= nr.w;
    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m *= m;
    return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = uv * size / unit;
    float h = snoise(float3(p, z)) * 0.68 + snoise(float3(p * 2.2 + 17.3, z * 1.6)) * 0.32;
    float v = h * Density;
    float fw = fwidth(v);
    float d = abs(frac(v - 0.5) - 0.5) / fw;
    float minor = 1.0 - smoothstep(0.4, 1.3, d);
    float major = 1.0 - smoothstep(0.9, 2.0, d);
    float index = floor(v + 0.5);
    float isMajor = 1.0 - step(0.5, index - 5.0 * floor(index / 5.0));
    float a = lerp(minor * 0.45, major, isMajor);
    a *= 1.0 - smoothstep(0.35, 0.8, fw);
    float vig = 1.0 - smoothstep(0.25, 0.85, length(uv - 0.5));
    float amount = saturate(((h * 0.5 + 0.5) * 0.05 * haze + a * Brightness * strength) * (0.35 + 0.65 * vig));
    float3 rgb = lerp(background.rgb, lineColor.rgb, amount);
    return float4(rgb, 1.0) * tex2D(input, uv).a;
}
