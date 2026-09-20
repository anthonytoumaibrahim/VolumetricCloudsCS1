// Draws nothing. Swapped into a game material slot when the draw call itself cannot be
// stopped: RainProperties.Update draws the game's rain shell unconditionally at the end of the
// same method that schedules lightning and thunder, and WeatherManager.EndRenderingImpl draws
// its bolt model in the same block as the strike's ground lights, so neither can simply be
// switched off. ZTest Never rejects every fragment; the vertex stage also throws the geometry
// out of clip space so nothing is even rasterised.
//
// _Color exists only because the game's bolt code reads material.color from whatever is in
// the slot; a shader without the property makes Unity log an error every frame.
Shader "VolumetricClouds/Invisible"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Never
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return float4(2, 2, 2, 1);
            }

            float4 frag() : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }

    Fallback Off
}
