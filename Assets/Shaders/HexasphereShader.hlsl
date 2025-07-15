Shader "Custom/Hexasphere"
{
    Properties
    {
        _BiomeAlbedoArray ("Biome Albedo Array", 2DArray) = "" {}
        _BiomeIndexTex    ("Tile → Biome map", 2D) = "white" {}
        _BiomeCount       ("BiomeCount", Int) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 4.5
        UNITY_DECLARE_TEX2DARRAY(_BiomeAlbedoArray);
        sampler2D _BiomeIndexTex;
        float _BiomeCount;

        struct Input
        {
            float2 uv_Mesh0;   // comes from Mesh UV (center of tile)
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Look up biome slice index by reading nearest pixel in index tex
            float2 uv = IN.uv_Mesh0;
            // _BiomeIndexTex stores biome id in R channel 0-1 → 0-255
            float biomeId = tex2D(_BiomeIndexTex, uv).r * 255.0; 
            int slice = clamp((int)biomeId, 0, (int)_BiomeCount - 1);

            float4 albedo = UNITY_SAMPLE_TEX2DARRAY(_BiomeAlbedoArray, float3(uv, slice));
            o.Albedo = albedo.rgb;
            o.Alpha = 1;
            o.Normal = float3(0,0,1);          // TODO add normal array later
        }
        ENDCG
    }
    FallBack "Diffuse"
}
