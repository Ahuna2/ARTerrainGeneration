Shader "Custom/TerrainShader" 
{
    Properties
    {
        _GrassColour("Grass Colour", Color) = (0,1,0,1)
        _RockColour("Rock Colour", Color) = (1,1,1,1)
        _SandColour("Sand Colour", Color) = (0,1,1,1)
        _SnowColour("Snow Colour", Color) = (1,0,0,0)
        _GrassSlopeThreshold("Grass Slope Threshold", Range(0,1)) = .5
        _GrassBlendAmount("Grass Blend Amount", Range(0,1)) = .5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        struct Input {
            float3 worldPos;
            float3 worldNormal;                
        };

        float _SnowHeightThreshold;
        float _SandHeightThreshold;
        half _GrassSlopeThreshold;
        half _GrassBlendAmount;
        fixed4 _GrassColour;
        fixed4 _RockColour;
        fixed4 _SandColour;
        fixed4 _SnowColour;

        void surf(Input IN, inout SurfaceOutputStandard o) {
            float slope = 1 - IN.worldNormal.y;
            float grassBlendHeight = _GrassSlopeThreshold * (1 - _GrassBlendAmount);
            float grassWeight = 1 - saturate((slope - grassBlendHeight) / (_GrassSlopeThreshold - grassBlendHeight));
            float snowWeight = clamp(IN.worldPos.y - _SnowHeightThreshold, 0, 1);
            fixed4 gradient = lerp(_SandColour, _GrassColour, clamp((IN.worldPos.y - _SandHeightThreshold), 0, 1));            
            o.Albedo = (gradient * grassWeight + _RockColour * (1 - grassWeight)) * (1 - snowWeight) + _SnowColour * snowWeight;
        }
        ENDCG
    }
}