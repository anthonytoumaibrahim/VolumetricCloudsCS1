// Draws nothing. Swapped into a game material slot when the draw call itself cannot be
// stopped: RainProperties.Update draws the game's rain shell unconditionally at the end of the
// same method that schedules lightning and thunder, so the component has to stay enabled.
// ZTest Never rejects every fragment; the vertex stage also throws the geometry out of clip
// space so nothing is even rasterised.
Shader "VolumetricClouds/Invisible"
{
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
