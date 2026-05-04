Shader "VVardenfell/MorrowindGpuParticle"
{
    Properties
    {
        _BaseArray ("Base Array", 2DArray) = "" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Particle
            {
                float3 Position;
                float Age;
                float3 Velocity;
                float Lifetime;
                float4 Color;
                float Size;
                int Bucket;
                int Slice;
                int Alive;
                int InstanceSlot;
                float RotationAngle;
                float RotationSpeed;
            };

            StructuredBuffer<Particle> _VfxParticles;
            TEXTURE2D_ARRAY(_BaseArray);
            SAMPLER(sampler_BaseArray);
            int _VfxParticleCount;
            int _VfxDrawBucket;
            float3 _VfxCameraRight;
            float3 _VfxCameraUp;

            struct Varyings
            {
                float4 PositionCS : SV_POSITION;
                float2 UV : TEXCOORD0;
                float4 Color : COLOR0;
                nointerpolation int Slice : TEXCOORD1;
                float Clip : TEXCOORD2;
            };

            static const float2 k_Corners[6] =
            {
                float2(-0.5, -0.5),
                float2( 0.5, -0.5),
                float2( 0.5,  0.5),
                float2(-0.5, -0.5),
                float2( 0.5,  0.5),
                float2(-0.5,  0.5)
            };

            Varyings vert(uint vertexId : SV_VertexID)
            {
                Varyings o;
                uint particleIndex = vertexId / 6;
                uint cornerIndex = vertexId - particleIndex * 6;
                Particle p = _VfxParticles[particleIndex];
                float2 corner = k_Corners[cornerIndex];
                float s;
                float c;
                sincos(p.RotationAngle, s, c);
                corner = float2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);

                float3 right = normalize(_VfxCameraRight);
                float3 up = normalize(_VfxCameraUp);
                float3 world = p.Position + (right * corner.x + up * corner.y) * max(0.001, p.Size);
                o.PositionCS = TransformWorldToHClip(world);
                o.UV = corner + 0.5;
                o.Color = p.Color;
                o.Slice = p.Slice;
                o.Clip = (p.Alive != 0 && p.Age >= 0.0 && p.Bucket == _VfxDrawBucket && particleIndex < (uint)_VfxParticleCount) ? 1.0 : -1.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.Clip);
                half4 tex = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, i.UV, i.Slice);
                return tex * i.Color;
            }
            ENDHLSL
        }
    }
}
