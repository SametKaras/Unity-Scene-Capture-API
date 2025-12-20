Shader "Custom/DepthGrayscale"
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
            };

            struct v2f
            {
                float4 vertex : SV_POSITION; // ekran pozisyonu
                float depth : TEXCOORD0; // derinlik bilgisi
            };

            float _MaxDepth;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // Unity's built-in macro for calculating eye-space depth
                COMPUTE_EYEDEPTH(o.depth);
                                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Use custom max depth if set, otherwise fall back to far clip plane
                float maxDepth = _MaxDepth;
                if (maxDepth < 0.01)
                {
                    maxDepth = _ProjectionParams.z; // Far clip plane
                }
                
                // Normalize depth by max depth
                float normalizedDepth = i.depth / maxDepth;
                
                // Clamp to 0-1 range
                normalizedDepth = saturate(normalizedDepth);
                
                // INVERT: Make closer objects WHITE, far objects DARK
                float depthValue = 1.0 - normalizedDepth;
                
                // Optional: Remap from [0,1] to [0.1,1.0] for better visual contrast
                // Comment out next line for full dynamic range (better for datasets)
                depthValue = depthValue * 0.9 + 0.1;
                
                // Output as grayscale
                return fixed4(depthValue, depthValue, depthValue, 1.0);
            }
            ENDCG
        }
    }
    
    // Fallback for objects without proper render type
    Fallback Off
}