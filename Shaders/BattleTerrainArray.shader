Shader "Battle/Tactical Terrain Array"
{
    Properties
    {
        _BaseColor("Tint",Color)=(1,1,1,1)
        _AlbedoArray("Albedo",2DArray)=""{}
        _NormalArray("Normal",2DArray)=""{}
        _MaskArray("Mask",2DArray)=""{}
        _HeightArray("Height",2DArray)=""{}
        _EmissiveArray("Emissive",2DArray)=""{}
        _Slice("Slice",Float)=0
        _Tiling("Tiling",Float)=1
        _NormalStrength("Normal Strength",Float)=1
        _HasSurface("Has Surface",Float)=0
        _HasNormal("Has Normal",Float)=0
        _HasMask("Has Mask",Float)=0
        _HasHeight("Has Height",Float)=0
        _HasEmissive("Has Emissive",Float)=0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Name "ForwardOnly" Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2DARRAY(_AlbedoArray);UNITY_DECLARE_TEX2DARRAY(_NormalArray);UNITY_DECLARE_TEX2DARRAY(_MaskArray);UNITY_DECLARE_TEX2DARRAY(_HeightArray);UNITY_DECLARE_TEX2DARRAY(_EmissiveArray);
            float4 _BaseColor;float _Slice;float _Tiling;float _NormalStrength;float _HasSurface;float _HasNormal;float _HasMask;float _HasHeight;float _HasEmissive;
            struct appdata{float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;};struct v2f{float4 position:SV_POSITION;float2 uv:TEXCOORD0;float3 normal:TEXCOORD1;};
            v2f vert(appdata v){v2f o;o.position=UnityObjectToClipPos(v.vertex);o.uv=v.uv*_Tiling;o.normal=UnityObjectToWorldNormal(v.normal);return o;}
            float4 frag(v2f i):SV_Target{if(_HasSurface<.5)return _BaseColor;float4 albedo=UNITY_SAMPLE_TEX2DARRAY(_AlbedoArray,float3(i.uv,_Slice));float3 packedNormal=_HasNormal>.5?UNITY_SAMPLE_TEX2DARRAY(_NormalArray,float3(i.uv,_Slice)).xyz*2-1:float3(0,0,1);float4 mask=_HasMask>.5?UNITY_SAMPLE_TEX2DARRAY(_MaskArray,float3(i.uv,_Slice)):float4(1,1,1,1);float height=_HasHeight>.5?UNITY_SAMPLE_TEX2DARRAY(_HeightArray,float3(i.uv,_Slice)).r:0;float3 emission=_HasEmissive>.5?UNITY_SAMPLE_TEX2DARRAY(_EmissiveArray,float3(i.uv,_Slice)).rgb:0;float light=.55+.45*saturate(dot(normalize(i.normal+packedNormal*_NormalStrength*.15),normalize(float3(.35,.85,.25))));return float4(albedo.rgb*_BaseColor.rgb*light*(.9+.1*mask.r)+emission+height*.015,1);}
            ENDHLSL
        }
    }
    Fallback "Unlit/Color"
}
