// Unlit shader that keys out a solid background color as transparent.
// Used for the Meta Animated Drawings video quad: exported MP4 has no alpha channel,
// so the Python renderer clears to _KeyColor instead, and this shader cuts it out at
// display time so the character reads as transparent against the Unity scene.
Shader "Custom/ChromaKeyUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _KeyColor ("Key Color", Color) = (1, 0, 1, 1)
        _Threshold ("Key Threshold", Range(0, 1)) = 0.25
        _Feather ("Key Feather", Range(0.001, 0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _KeyColor;
            float _Threshold;
            float _Feather;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float dist = distance(col.rgb, _KeyColor.rgb);
                float alpha = smoothstep(_Threshold, _Threshold + _Feather, dist);
                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }
}
