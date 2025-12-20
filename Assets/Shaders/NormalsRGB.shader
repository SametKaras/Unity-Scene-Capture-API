Shader "Custom/NormalsRGB"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION; // ekran pozisyonu
                float3 worldNormal : TEXCOORD0; // normal bilgisi
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // Transform normal from object space to world space
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Normalize the interpolated normal (important after rasterization)
                float3 normal = normalize(i.worldNormal);

                // Map normal from [-1, 1] to [0, 1] for RGB encoding
                // Standard encoding: R=X, G=Y, B=Z in world space
                float3 encodedNormal = normal * 0.5 + 0.5;

                // Output as RGB color
                return fixed4(encodedNormal, 1.0);
            }
            ENDCG
        }
    }
    
    // Fallback for objects without proper render type
    Fallback Off
}