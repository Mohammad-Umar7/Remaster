// Full-screen pass that turns the 3D render into a pencil-and-ink drawing:
// wobbly outlines from depth + normals (two passes, like a line drawn twice),
// "boiling" at a low frame rate, then paper grain on top.
Shader "Hidden/Remaster/SketchPost"
{
    Properties { _MainTex ("", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_TexelSize;
            sampler2D _CameraDepthNormalsTexture;
            sampler2D _RM_Noise, _RM_PaperTex;
            float4 _RM_Ink, _RM_Pencil, _RM_Paper;
            float _Thick, _Wobble, _BoilFps, _DepthSens, _NormalSens, _PaperAmt, _Flash, _Desat, _HaloRadius, _HaloAmt;
            float4 _FlashColor;

            void sampleDN(float2 uv, out float depth, out float3 n)
            {
                float4 e = tex2D(_CameraDepthNormalsTexture, uv);
                DecodeDepthNormal(e, depth, n);
            }

            float edgeAt(float2 uv, float thick)
            {
                float2 o = _MainTex_TexelSize.xy * thick;
                float d0, d1, d2, d3, dc; float3 n0, n1, n2, n3, nc;
                sampleDN(uv + o * float2( 1,  1), d0, n0);
                sampleDN(uv + o * float2(-1, -1), d1, n1);
                sampleDN(uv + o * float2(-1,  1), d2, n2);
                sampleDN(uv + o * float2( 1, -1), d3, n3);
                sampleDN(uv, dc, nc);
                float nd = length(n0 - n1) + length(n2 - n3);
                float dd = abs(d0 - d1) + abs(d2 - d3);
                // surfaces seen at a grazing angle change depth quickly: relax the threshold there
                float graze = 1.0 - saturate(abs(nc.z));
                float dThresh = _DepthSens * max(dc, 0.0005) * (1.0 + 14.0 * graze * graze);
                // depth is stored in 16 bits: right in front of the eye (the handheld) a slope steps by a bit or two,
                // which must not be read as an edge
                dThresh = max(dThresh, 3.0 / 65535.0);
                float en = smoothstep(_NormalSens, _NormalSens * 1.7, nd);
                float ed = smoothstep(dThresh, dThresh * 2.0, dd);
                return saturate(max(en, ed));
            }

            float minFlag(float2 uv, float thick)
            {
                float2 o = _MainTex_TexelSize.xy * thick;
                float a = tex2D(_MainTex, uv).a;
                a = min(a, tex2D(_MainTex, uv + o * float2( 1,  1)).a);
                a = min(a, tex2D(_MainTex, uv + o * float2(-1, -1)).a);
                a = min(a, tex2D(_MainTex, uv + o * float2(-1,  1)).a);
                a = min(a, tex2D(_MainTex, uv + o * float2( 1, -1)).a);
                return a;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                float2 suv = uv * float2(aspect, 1.0);
                float t = floor(_Time.y * _BoilFps);
                float2 j1 = frac(t * float2(0.371, 0.713));
                float2 j2 = frac(t * float2(0.137, 0.579) + 0.31);

                float2 w1 = tex2D(_RM_Noise, suv * 1.9 + j1).rg - 0.5;
                float2 w2 = tex2D(_RM_Noise, suv * 1.1 + j2).gr - 0.5;
                float2 uvA = uv + w1 * _Wobble * _MainTex_TexelSize.xy;
                float2 uvB = uv + w2 * _Wobble * 2.6 * _MainTex_TexelSize.xy + _MainTex_TexelSize.xy * float2(1.5, -1.0);

                float eA = edgeAt(uvA, _Thick);
                float eB = edgeAt(uvB, _Thick * 0.75);

                fixed4 col = tex2D(_MainTex, uv);
                float flag = min(minFlag(uvA, _Thick), minFlag(uvB, _Thick * 0.75));

                // pencil pressure: lines break up a little with the paper tooth
                float tooth = tex2D(_RM_PaperTex, suv * 3.1 + j1 * 0.5).a;
                float press = tex2D(_RM_Noise, suv * 0.9 + j2).b;

                float inkAmt = saturate(eA * 0.96 + eB * 0.38) * lerp(0.80, 1.0, tooth);
                float pencilAmt = saturate(eA * 0.80 + eB * 0.55) * lerp(0.45, 1.0, tooth) * lerp(0.7, 1.0, press);

                float isPencil = smoothstep(0.25, 0.45, flag) * (1.0 - smoothstep(0.75, 0.9, flag));
                float isInk = 1.0 - smoothstep(0.25, 0.45, flag);
                col.rgb = lerp(col.rgb, _RM_Ink.rgb, inkAmt * isInk);
                col.rgb = lerp(col.rgb, _RM_Pencil.rgb, pencilAmt * isPencil);

                // depth shading: whatever sits behind something closer gets a soft pencil shadow around
                // that thing, which separates the layers of the scene and makes it read as deep
                {
                    float dC; float3 nC; sampleDN(uv, dC, nC);
                    float2 r = _MainTex_TexelSize.xy * _HaloRadius;
                    float occ = 0.0;
                    const float2 dirs[8] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1), float2(0.7,0.7), float2(-0.7,0.7), float2(0.7,-0.7), float2(-0.7,-0.7) };
                    for (int k = 0; k < 8; k++)
                    {
                        float d; float3 nn;
                        sampleDN(uv + dirs[k] * r * (0.6 + 0.4 * tooth), d, nn);
                        occ += saturate((dC - d) / (dC * 0.22 + 0.0005));
                    }
                    occ = occ / 8.0;
                    occ *= (dC > 0.985) ? 0.45 : 1.0;      // lighter on the open sky
                    float shadeTex = lerp(0.75, 1.0, tex2D(_RM_Noise, suv * 7.0).r);
                    col.rgb *= lerp(float3(1, 1, 1), float3(0.62, 0.60, 0.74), saturate(occ * _HaloAmt) * shadeTex);
                }

                // paper grain + soft pencil vignette
                float3 paper = tex2D(_RM_PaperTex, suv * 1.35).rgb;
                col.rgb *= lerp(float3(1, 1, 1), paper, _PaperAmt);
                float2 vq = uv - 0.5;
                float vig = smoothstep(0.42, 0.80, length(vq * float2(1.0, 1.08)));
                float scrib = tex2D(_RM_Noise, suv * 5.0 + j1).b;
                col.rgb = lerp(col.rgb, col.rgb * lerp(0.80, 0.93, scrib), vig * 0.55);

                // optional washes (hurt flash, page flash, sleepy desaturation)
                float lum = dot(col.rgb, float3(0.3, 0.59, 0.11));
                col.rgb = lerp(col.rgb, float3(lum, lum, lum) * _RM_Paper.rgb * 1.05, _Desat);
                col.rgb = lerp(col.rgb, _FlashColor.rgb, _Flash);
                col.a = 1;
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
