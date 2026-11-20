// Black-level lift for the background video. The clip's black comes out of the encoder
// as roughly (16,16,16); everything at or below `lift` is pulled down to true black and
// the rest is stretched to keep the full range. Compiled with fxc (ps_2_0) to
// BlackLevel.ps, which BlackLevelEffect loads as an application resource.
//
//   fxc /T ps_2_0 /E main /Fo BlackLevel.ps BlackLevel.fx

sampler2D input : register(s0);
float lift : register(c0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    float3 rgb = saturate((c.rgb - lift) / max(1.0 - lift, 0.0001));
    return float4(rgb, c.a);
}
