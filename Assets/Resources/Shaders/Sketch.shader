// Hand-drawn surface shader for the 3D remake world.
// Colour comes out flat like marker pen, shaded with pencil hatching.
// Alpha channel carries a "line flag" for the post effect:
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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _RM_Hatch, _RM_Grid, _RM_Noise;
            fixed4 _Color;
            float _Mode, _Flag, _Dissolve, _Ghost, _Reveal, _HatchScale;
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
                o.color = v.color;
                return o;
            }

            float dither4(float2 p)
            {
                // 4x4 ordered dither
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
                    float ndl = dot(n, normalize(_RM_LightDir.xyz));
                    // finer strokes up close, broader ones far away, so hatching never turns into giant scratches
                    float lod = saturate((dist - 1.5) / 7.0);
                    float4 h = lerp(tex2D(_RM_Hatch, uvT * _HatchScale * 3.1), tex2D(_RM_Hatch, uvT * _HatchScale), lod);
                    float streak = tex2D(_RM_Noise, uvT * float2(0.07, 0.33)).g;
                    col = base * lerp(0.93, 1.05, streak);
                    // three tones: lit, half (single hatch), shade (cross hatch)
                    float mid = smoothstep(0.55, 0.35, ndl);
                    float dark = smoothstep(0.05, -0.15, ndl);
                    float hatch1 = h.r;
                    float hatch2 = max(h.r, h.g);
                    col = lerp(col, col * lerp(0.92, 0.60, hatch1), mid);
                    col = lerp(col, col * lerp(0.82, 0.48, hatch2), dark);
                }

                // fade into the blank page: vertex alpha (cliffs) and distance
                float fog = saturate((dist - _RM_FogStart) / max(_RM_FogEnd - _RM_FogStart, 0.001));
                float fade = saturate(max(1.0 - i.color.a, fog * fog));
                col = lerp(col, _RM_Paper.rgb, fade);
                float flag = lerp(_Flag, 1.0, smoothstep(0.75, 1.0, fade));
                return fixed4(col, flag);
            }
            ENDCG
        }
    }
    FallBack Off
}
