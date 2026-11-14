// Hand-drawn surface shader for the 3D remake world.
// Marker-pen colour, pencil hatching for the shaded sides, and real cast shadows from the sun
// (drawn as cross-hatched, slightly cool areas) so the space reads as solid and deep.
// Alpha carries a "line flag" for the post effect:
//   0.0 = ink outline, 0.5 = blue pencil outline (unfinished), 1.0 = no outline.
Shader "Remaster/Sketch"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Mode ("Mode (0 marker, 1 grid greybox, 2 flat unlit)", Float) = 0
        _Flag ("Line flag", Float) = 0
        _Dissolve ("Dissolve near camera", Float) = 0
        _Ghost ("Ghost dither", Float) = 0
        _Reveal ("Reveal 0..1", Float) = 1
        _HatchScale ("Hatch scale", Float) = 0.35
        _AO ("Darken toward the base (unit cube)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _RM_Hatch, _RM_Grid, _RM_Noise;
            fixed4 _Color;
            float _Mode, _Flag, _Dissolve, _Ghost, _Reveal, _HatchScale, _AO;
            float4 _RM_Paper, _RM_GridFill, _RM_GridLine, _RM_LightDir;
            float _RM_FogStart, _RM_FogEnd;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float3 wn : TEXCOORD2;
                float4 sp : TEXCOORD3;
                SHADOW_COORDS(4)
                float objY : TEXCOORD5;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.sp = ComputeScreenPos(o.pos);
                o.objY = v.vertex.y;
                o.color = v.color;
                TRANSFER_SHADOW(o);
                return o;
            }

            float dither4(float2 p)
            {
                float2 q = floor(fmod(p, 4.0));
                float m = dot(q, float2(1.0, 4.0));
                const float t[16] = { 0,8,2,10, 12,4,14,6, 3,11,1,9, 15,7,13,5 };
                return (t[(int)m] + 0.5) / 16.0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.wn);
                float3 an = abs(n);
                float2 uvT = (an.y > 0.6) ? i.wp.xz : ((an.x > 0.6) ? i.wp.zy : i.wp.xy);
                float2 pix = i.sp.xy / max(i.sp.w, 0.0001) * _ScreenParams.xy;
                float dth = dither4(pix);
                float dist = distance(_WorldSpaceCameraPos, i.wp);

                if (_Dissolve > 0.5) clip(dist - 0.25 - dth * 1.1);
                if (_Ghost > 0.5) clip(dth - 0.62);
                if (_Reveal < 0.999)
                {
                    float nz = tex2D(_RM_Noise, uvT * 0.21).r;
                    clip(_Reveal - nz * 0.98 - 0.01);
                }

                fixed4 tex = tex2D(_MainTex, i.uv);
                float3 base = _Color.rgb * tex.rgb * i.color.rgb;
                fixed atten = SHADOW_ATTENUATION(i);

                float3 col;
                if (_Mode > 1.5)
                {
                    col = base;
                }
                else
                {
                    if (_Mode > 0.5)
                    {
                        float g = tex2D(_RM_Grid, uvT * 0.5).r;
                        base = lerp(_RM_GridLine.rgb, _RM_GridFill.rgb, g) * lerp(1.0, _Color.rgb, 0.35);
                    }
                    float3 L = (dot(_WorldSpaceLightPos0.xyz, _WorldSpaceLightPos0.xyz) > 0.01) ? normalize(_WorldSpaceLightPos0.xyz) : normalize(_RM_LightDir.xyz);
                    float ndl = dot(n, L);
                    // a surface in cast shadow is drawn like a surface facing away from the sun
                    float lit = min(ndl, lerp(-0.35, 1.0, atten));

                    // finer strokes up close, broader ones far away, so hatching never turns into giant scratches
                    float lod = saturate((dist - 1.5) / 7.0);
                    float4 h = lerp(tex2D(_RM_Hatch, uvT * _HatchScale * 3.1), tex2D(_RM_Hatch, uvT * _HatchScale), lod);
                    float streak = tex2D(_RM_Noise, uvT * float2(0.07, 0.33)).g;
                    col = base * lerp(0.94, 1.05, streak);

                    float mid = smoothstep(0.55, 0.30, lit);
                    float dark = smoothstep(0.08, -0.12, lit);
                    float hatch1 = h.r;
                    float hatch2 = max(h.r, h.g);
                    col = lerp(col, col * lerp(0.93, 0.66, hatch1), mid);
                    col = lerp(col, col * lerp(0.78, 0.50, hatch2) * float3(0.90, 0.88, 1.0), dark);

                    // walls get heavier toward the ground, which plants them on the floor
                    if (_AO > 0.5) col *= lerp(0.72, 1.0, smoothstep(-0.5, -0.10, i.objY));
                }

                // things fade into the blank page: vertex alpha (cliffs) and distance (air)
                float fog = saturate((dist - _RM_FogStart) / max(_RM_FogEnd - _RM_FogStart, 0.001));
                float air = saturate((dist - 9.0) / 60.0) * 0.22;
                // cliffs: first they go dark (depth), then they dissolve into the page (nothing was drawn there)
                float cliff = 1.0 - i.color.a;
                col *= lerp(1.0, 0.50, smoothstep(0.0, 0.45, cliff));
                float fade = saturate(max(smoothstep(0.35, 1.0, cliff), max(fog * fog, air)));
                col = lerp(col, _RM_Paper.rgb, fade);
                float flag = lerp(_Flag, 1.0, smoothstep(0.75, 1.0, fade));
                return fixed4(col, flag);
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vertSC
            #pragma fragment fragSC
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct v2fSC { V2F_SHADOW_CASTER; };
            v2fSC vertSC(appdata_base v)
            {
                v2fSC o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 fragSC(v2fSC i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack Off
}
