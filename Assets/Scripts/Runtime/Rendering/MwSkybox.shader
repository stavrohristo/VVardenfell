Shader "VVardenfell/MwSkybox"
{
    Properties
    {
        _SkyColor ("Sky Color", Color) = (0.37, 0.53, 0.8, 1)
        _SunDiscColor ("Sun Disc Color", Color) = (1, 0.95, 0.85, 1)
        _SunTex ("Sun", 2D) = "white" {}
        _SunGlareTex ("Sun Glare", 2D) = "white" {}
        _MasserTex ("Masser", 2D) = "white" {}
        _SecundaTex ("Secunda", 2D) = "white" {}
        _CloudTex ("Clouds", 2D) = "white" {}
        _SunDirection ("Sun Direction", Vector) = (0, 1, 0, 0)
        _MasserDirection ("Masser Direction", Vector) = (0, 1, 0, 0)
        _SecundaDirection ("Secunda Direction", Vector) = (0, 1, 0, 0)
        _MoonWeather ("Moon Weather", Vector) = (0, 0, 0, 0)
        _CloudWeather ("Cloud Weather", Vector) = (0, 0, 0, 0)
        _SkyWeather ("Sky Weather", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SunTex);
            SAMPLER(sampler_SunTex);
            TEXTURE2D(_SunGlareTex);
            SAMPLER(sampler_SunGlareTex);
            TEXTURE2D(_MasserTex);
            SAMPLER(sampler_MasserTex);
            TEXTURE2D(_SecundaTex);
            SAMPLER(sampler_SecundaTex);
            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyColor;
                float4 _SunDiscColor;
                float4 _SunDirection;
                float4 _MasserDirection;
                float4 _SecundaDirection;
                float4 _MoonWeather;
                float4 _CloudWeather;
                float4 _SkyWeather;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = normalize(input.positionOS.xyz);
                return output;
            }

            float2 DirectionToLatLong(float3 direction)
            {
                const float invTwoPi = 0.15915494309189535;
                const float invPi = 0.3183098861837907;
                return float2(atan2(direction.x, direction.z) * invTwoPi + 0.5, asin(clamp(direction.y, -1.0, 1.0)) * invPi + 0.5);
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Hash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash(i + float3(0, 0, 0));
                float n100 = Hash(i + float3(1, 0, 0));
                float n010 = Hash(i + float3(0, 1, 0));
                float n110 = Hash(i + float3(1, 1, 0));
                float n001 = Hash(i + float3(0, 0, 1));
                float n101 = Hash(i + float3(1, 0, 1));
                float n011 = Hash(i + float3(0, 1, 1));
                float n111 = Hash(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float Fbm(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    value += ValueNoise(p) * amplitude;
                    p = p * 2.03 + 17.1;
                    amplitude *= 0.5;
                }
                return value;
            }

            float4 SampleDisc(Texture2D tex, SamplerState texSampler, float3 direction, float3 center, float angularRadius)
            {
                float3 up = abs(center.y) > 0.95 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 right = normalize(cross(up, center));
                float3 discUp = normalize(cross(center, right));
                float2 planar = float2(dot(direction, right), dot(direction, discUp)) / max(0.0001, angularRadius);
                float2 uv = planar * 0.5 + 0.5;
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                float facing = smoothstep(cos(angularRadius), 1.0, dot(direction, center));
                float4 sample = SAMPLE_TEXTURE2D(tex, texSampler, uv);
                sample.a *= inside * facing;
                sample.rgb *= inside;
                return sample;
            }

            float CloudTextureMask(float3 direction, float offset)
            {
                float denominator = max(0.18, direction.y + 0.34);
                float2 uv = direction.xz / denominator;
                uv = uv * 0.22 + float2(offset * 0.08, offset * 0.031);

                float4 sample = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv);
                float luminance = dot(sample.rgb, float3(0.299, 0.587, 0.114));
                return saturate(max(sample.a, (luminance - 0.28) * 1.65));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionWS);
                float aboveHorizon = smoothstep(-0.06, 0.12, direction.y);
                float vertical = saturate(direction.y * 0.5 + 0.5);
                float zenith = smoothstep(0.04, 0.96, vertical);
                float dayAmount = saturate(_SkyWeather.z + (1.0 - _SkyWeather.y) * 0.2);
                float lightning = saturate(_SkyWeather.w);

                float3 skyColor = max(_SkyColor.rgb, 0.0001);
                float3 horizonColor = lerp(skyColor * 0.62, lerp(skyColor, float3(0.78, 0.80, 0.76), 0.22), dayAmount);
                float3 zenithColor = lerp(skyColor * 0.82, skyColor * 1.16, dayAmount);
                float3 sky = lerp(horizonColor, zenithColor, zenith);
                sky = lerp(sky, float3(1, 1, 1), lightning * 0.42);

                float3 sunDir = normalize(_SunDirection.xyz);
                float sunOpacity = saturate(_SkyWeather.z);
                float sunGlare = saturate(_SunDirection.w);
                float4 sun = SampleDisc(_SunTex, sampler_SunTex, direction, sunDir, 0.034) * sunOpacity;
                float sunDot = max(0.0, dot(direction, sunDir));
                float glare = pow(sunDot, 96.0) * sunGlare * sunOpacity * 0.38;

                float cloudOffset = _CloudWeather.y;
                float upperSky = smoothstep(-0.03, 0.42, direction.y);
                float cloudNoise = Fbm(direction * 3.2 + float3(cloudOffset * 0.19, cloudOffset * 0.03, cloudOffset * 0.11));
                float cloudTexture = CloudTextureMask(direction, cloudOffset);
                float cloudShape = saturate((cloudNoise * 0.68 + cloudTexture * 0.58 - 0.54) * 2.15);
                float cloudAlpha = saturate(_CloudWeather.x) * upperSky * cloudShape * 0.68;
                float3 cloudColor = lerp(skyColor * 1.08, float3(0.92, 0.94, 0.90), dayAmount * 0.65);
                cloudColor = lerp(cloudColor, float3(1, 1, 1), lightning * 0.55);
                sky = lerp(sky, cloudColor, cloudAlpha);

                float starOpacity = saturate(_SkyWeather.y);
                float2 starUv = DirectionToLatLong(direction);
                starUv.x += _SkyWeather.x / 360.0;
                float starSeed = Hash(floor(starUv.xyx * float3(760.0, 420.0, 17.0)));
                float star = smoothstep(0.9986, 1.0, starSeed) * smoothstep(0.02, 0.34, direction.y);
                sky += star * starOpacity * 0.16;

                float3 masserDir = normalize(_MasserDirection.xyz);
                float3 secundaDir = normalize(_SecundaDirection.xyz);
                float masserAlpha = saturate(_MoonWeather.x);
                float secundaAlpha = saturate(_MoonWeather.y);
                float masserRadius = max(0.012, _MasserDirection.w);
                float secundaRadius = max(0.008, _SecundaDirection.w);
                float4 masser = SampleDisc(_MasserTex, sampler_MasserTex, direction, masserDir, masserRadius) * masserAlpha;
                float4 secunda = SampleDisc(_SecundaTex, sampler_SecundaTex, direction, secundaDir, secundaRadius) * secundaAlpha;

                sky = lerp(sky, masser.rgb, saturate(masser.a));
                sky = lerp(sky, secunda.rgb, saturate(secunda.a));
                sky = lerp(sky, _SunDiscColor.rgb, saturate(glare));
                sky = lerp(sky, _SunDiscColor.rgb * max(sun.rgb, 0.2), saturate(sun.a));

                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
