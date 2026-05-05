using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    [UpdateAfter(typeof(EnvironmentPresentationSystem))]
    public partial class SkyWeatherPresentationSystem : SystemBase
    {
        static readonly ProfilerMarker k_SyncSkyWeather = new("VV.SkyWeather.Sync");

        SkyWeatherRig _rig;
        Material _previousSkybox;
        bool _capturedPreviousSkybox;

        protected override void OnCreate()
        {
            RequireForUpdate<ActiveSkyWeatherState>();
            RequireForUpdate<MainCameraSingleton>();
        }

        protected override void OnDestroy()
        {
            if (_capturedPreviousSkybox && _rig != null && _rig.IsCurrentSkybox)
                RenderSettings.skybox = _previousSkybox;
            _rig?.Dispose();
            _rig = null;
        }

        protected override void OnUpdate()
        {
            using var _ = k_SyncSkyWeather.Auto();

            CompleteDependency();

            Camera camera = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
            _rig ??= new SkyWeatherRig();
            if (!_capturedPreviousSkybox)
            {
                _previousSkybox = RenderSettings.skybox;
                _capturedPreviousSkybox = true;
            }
            MorrowindDayCycleState settings = SystemAPI.HasSingleton<MorrowindDayCycleState>()
                ? SystemAPI.GetSingleton<MorrowindDayCycleState>()
                : LightingBootstrapSystem.CreateDefaultDayCycle();
            _rig.Apply(SystemAPI.GetSingleton<ActiveSkyWeatherState>(), settings, camera);
        }

        sealed class SkyWeatherRig
        {
            const float MinimumSkyDistance = 64f;
            const float MoonScale = 1.1f;
            const float MoonAngularDegreesScale = 0.36f;
            const float MinimumCloudOpacity = 0.12f;
            const float MinimumMoonOpacity = 0.03f;
            const float DefaultMoonBrightness = 2.75f;
            const float DefaultMoonEmission = 1.35f;
            const float DefaultMoonMaskScale = 1f;
            const float DefaultStarBrightness = 1.6f;
            const float DefaultStarTextureOpacity = 1f;
            const float DefaultStarTextureScale = 7f;
            const float DefaultPrecipitationEmissionScale = 1f;
            const float DefaultPrecipitationWorldScale = WorldScale.MwUnitsToMeters;
            const float DefaultPrecipitationParticleSizeScale = 1f;

            readonly Shader _shader;
            readonly Shader _skyboxShader;
            readonly List<Texture2D> _ownedTextures = new();
            readonly Texture2D _whiteTexture;
            Texture2D _sunTexture;
            Texture2D _sunGlareTexture;
            Texture2D _starTexture;
            Texture2D _rainTexture;
            Texture2D _masserMaskTexture;
            Texture2D _secundaMaskTexture;
            Texture2D[] _masserPhaseTextures;
            Texture2D[] _secundaPhaseTextures;
            Texture2D[] _cloudTextures;
            Texture2D[] _precipitationTextures;
            string[] _precipitationTexturePaths;
            string[] _cloudTexturePaths;
            string[] _cloudWeatherIds;
            int _weatherDefinitionCount;
            bool _bakedTexturesLoaded;

            GameObject _root;
            Transform _rootTransform;
            Transform _skyTransform;
            Transform _starTransform;
            Transform _cloudTransform;
            Transform _sunTransform;
            Transform _glareTransform;
            Transform _masserTransform;
            Transform _secundaTransform;
            float _skyDistance = 256f;
            MeshRenderer _skyRenderer;
            MeshRenderer _starRenderer;
            MeshRenderer _cloudRenderer;
            MeshRenderer _sunRenderer;
            MeshRenderer _glareRenderer;
            MeshRenderer _masserRenderer;
            MeshRenderer _secundaRenderer;
            Material _skyMaterial;
            Material _starMaterial;
            Material _cloudMaterial;
            Material _sunMaterial;
            Material _glareMaterial;
            Material _masserMaterial;
            Material _secundaMaterial;
            Material _precipitationMaterial;
            Material _skyboxMaterial;
            ParticleSystem _precipitation;
            ParticleSystemRenderer _precipitationRenderer;

            static readonly int k_SkyboxSkyColorId = Shader.PropertyToID("_SkyColor");
            static readonly int k_SkyboxSunDiscColorId = Shader.PropertyToID("_SunDiscColor");
            static readonly int k_SkyboxSunTexId = Shader.PropertyToID("_SunTex");
            static readonly int k_SkyboxSunGlareTexId = Shader.PropertyToID("_SunGlareTex");
            static readonly int k_SkyboxStarTexId = Shader.PropertyToID("_StarTex");
            static readonly int k_SkyboxMasserTexId = Shader.PropertyToID("_MasserTex");
            static readonly int k_SkyboxSecundaTexId = Shader.PropertyToID("_SecundaTex");
            static readonly int k_SkyboxMasserMaskTexId = Shader.PropertyToID("_MasserMaskTex");
            static readonly int k_SkyboxSecundaMaskTexId = Shader.PropertyToID("_SecundaMaskTex");
            static readonly int k_SkyboxCloudTexId = Shader.PropertyToID("_CloudTex");
            static readonly int k_SkyboxNextCloudTexId = Shader.PropertyToID("_NextCloudTex");
            static readonly int k_SkyboxSunDirectionId = Shader.PropertyToID("_SunDirection");
            static readonly int k_SkyboxMasserDirectionId = Shader.PropertyToID("_MasserDirection");
            static readonly int k_SkyboxSecundaDirectionId = Shader.PropertyToID("_SecundaDirection");
            static readonly int k_SkyboxMoonWeatherId = Shader.PropertyToID("_MoonWeather");
            static readonly int k_SkyboxMoonPresentationId = Shader.PropertyToID("_MoonPresentation");
            static readonly int k_SkyboxStarPresentationId = Shader.PropertyToID("_StarPresentation");
            static readonly int k_SkyboxCloudWeatherId = Shader.PropertyToID("_CloudWeather");
            static readonly int k_SkyboxWeatherId = Shader.PropertyToID("_SkyWeather");

            public SkyWeatherRig()
            {
                _shader = FindRequiredShader("VVardenfell/SkyWeatherUnlit");
                _skyboxShader = FindRequiredShader("VVardenfell/MwSkybox");
                _whiteTexture = Own(CreateSolidTexture(new Color(1f, 1f, 1f, 1f)));
                _sunTexture = Own(CreateSunTexture(128));
                _sunGlareTexture = _sunTexture;
                _starTexture = null;
                _rainTexture = Own(CreateRainTexture(32));
                _masserMaskTexture = null;
                _secundaMaskTexture = null;
                _masserPhaseTextures = Array.Empty<Texture2D>();
                _secundaPhaseTextures = Array.Empty<Texture2D>();
                _cloudTextures = Array.Empty<Texture2D>();
                _precipitationTextures = Array.Empty<Texture2D>();
                _precipitationTexturePaths = Array.Empty<string>();
                _cloudTexturePaths = Array.Empty<string>();
                _cloudWeatherIds = Array.Empty<string>();
                _skyboxMaterial = new Material(_skyboxShader)
                {
                    name = "VVardenfell.Runtime.MwSkybox",
                };
            }

            public bool IsCurrentSkybox => RenderSettings.skybox == _skyboxMaterial;

            public void Apply(in ActiveSkyWeatherState sky, in MorrowindDayCycleState settings, Camera camera)
            {
                if (!EnsureBakedTexturesLoaded())
                    return;

                EnsureRig();

                bool exterior = sky.IsInterior == 0;
                ApplySkybox(sky, settings, exterior);
                _root.SetActive(exterior);
                if (!exterior)
                {
                    StopPrecipitation();
                    return;
                }

                Vector3 cameraPosition = camera.transform.position;
                _rootTransform.position = cameraPosition;
                SetSkyRigRenderersEnabled(false);
                ApplyPrecipitation(sky, settings, camera);
            }

            public void Dispose()
            {
                DestroyObject(_root);
                DestroyObject(_skyMaterial);
                DestroyObject(_starMaterial);
                DestroyObject(_cloudMaterial);
                DestroyObject(_sunMaterial);
                DestroyObject(_glareMaterial);
                DestroyObject(_masserMaterial);
                DestroyObject(_secundaMaterial);
                DestroyObject(_precipitationMaterial);
                DestroyObject(_skyboxMaterial);
                if (_precipitation != null)
                    DestroyObject(_precipitation.gameObject);
                for (int i = 0; i < _ownedTextures.Count; i++)
                {
                    if (_ownedTextures[i] != null)
                        DestroyObject(_ownedTextures[i]);
                }
                _ownedTextures.Clear();
            }

            void EnsureRig()
            {
                if (_root != null)
                    return;

                _root = new GameObject("VVardenfell.SkyWeatherRig");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                _rootTransform = _root.transform;

                _skyMaterial = CreateMaterial(_whiteTexture, (int)RenderQueue.Background);
                _starMaterial = CreateMaterial(_starTexture, (int)RenderQueue.Background + 10);
                _cloudMaterial = CreateMaterial(_whiteTexture, (int)RenderQueue.Background + 20);
                _sunMaterial = CreateMaterial(_sunTexture, (int)RenderQueue.Background + 30);
                _glareMaterial = CreateMaterial(_sunGlareTexture, (int)RenderQueue.Background + 31);
                _masserMaterial = CreateMaterial(_masserPhaseTextures[0], (int)RenderQueue.Background + 32);
                _secundaMaterial = CreateMaterial(_secundaPhaseTextures[0], (int)RenderQueue.Background + 33);
                _precipitationMaterial = CreateMaterial(_rainTexture, (int)RenderQueue.Transparent);

                _skyRenderer = CreatePrimitive("Atmosphere", PrimitiveType.Sphere, _skyMaterial, out _skyTransform);
                _starRenderer = CreatePrimitive("Stars", PrimitiveType.Sphere, _starMaterial, out _starTransform);
                _cloudRenderer = CreatePrimitive("Clouds", PrimitiveType.Sphere, _cloudMaterial, out _cloudTransform);
                _sunRenderer = CreatePrimitive("Sun", PrimitiveType.Quad, _sunMaterial, out _sunTransform);
                _glareRenderer = CreatePrimitive("SunGlare", PrimitiveType.Quad, _glareMaterial, out _glareTransform);
                _masserRenderer = CreatePrimitive("Masser", PrimitiveType.Quad, _masserMaterial, out _masserTransform);
                _secundaRenderer = CreatePrimitive("Secunda", PrimitiveType.Quad, _secundaMaterial, out _secundaTransform);
                _precipitation = CreatePrecipitation(_precipitationMaterial);
                UnityEngine.Object.DontDestroyOnLoad(_precipitation.gameObject);
            }

            bool EnsureBakedTexturesLoaded()
            {
                if (_bakedTexturesLoaded)
                    return true;

                var cache = WorldResources.Cache;
                var contentBlob = cache?.ContentBlob ?? default;
                if (cache == null || !contentBlob.IsCreated || cache.Textures == null)
                    return false;

                LoadBakedTexturesOrThrow(cache, contentBlob);
                _bakedTexturesLoaded = true;
                return true;
            }

            void SetSkyRigRenderersEnabled(bool enabled)
            {
                if (_skyRenderer != null)
                    _skyRenderer.enabled = enabled;
                if (_starRenderer != null)
                    _starRenderer.enabled = enabled;
                if (_cloudRenderer != null)
                    _cloudRenderer.enabled = enabled;
                if (_sunRenderer != null)
                    _sunRenderer.enabled = enabled;
                if (_glareRenderer != null)
                    _glareRenderer.enabled = enabled;
                if (_masserRenderer != null)
                    _masserRenderer.enabled = enabled;
                if (_secundaRenderer != null)
                    _secundaRenderer.enabled = enabled;
            }

            void ApplySkybox(in ActiveSkyWeatherState sky, in MorrowindDayCycleState settings, bool exterior)
            {
                if (_skyboxMaterial == null)
                    return;

                if (RenderSettings.skybox != _skyboxMaterial)
                    RenderSettings.skybox = _skyboxMaterial;

                Texture2D cloudTexture = ResolveCloudTexture(sky.CurrentCloudTextureIndex);
                Texture2D nextCloudTexture = ResolveNextCloudTexture(sky.CurrentCloudTextureIndex, sky.NextCloudTextureIndex, sky.WeatherTransition, cloudTexture);
                int masserPhase = math.clamp(sky.MasserPhase, 0, _masserPhaseTextures.Length - 1);
                int secundaPhase = math.clamp(sky.SecundaPhase, 0, _secundaPhaseTextures.Length - 1);
                float cloudOpacity = exterior && sky.CloudOpacity > 0.001f ? math.max(MinimumCloudOpacity, math.saturate(sky.CloudOpacity)) : 0f;
                float masserOpacity = exterior && sky.MasserOpacity > 0.001f ? math.max(MinimumMoonOpacity, math.saturate(sky.MasserOpacity)) : 0f;
                float secundaOpacity = exterior && sky.SecundaOpacity > 0.001f ? math.max(MinimumMoonOpacity, math.saturate(sky.SecundaOpacity)) : 0f;
                float moonBrightness = settings.MoonBrightnessScale > 0f ? settings.MoonBrightnessScale : DefaultMoonBrightness;
                float moonEmission = settings.MoonEmissionScale > 0f ? settings.MoonEmissionScale : DefaultMoonEmission;
                float moonMaskScale = settings.MoonMaskScale > 0f ? settings.MoonMaskScale : DefaultMoonMaskScale;
                float starBrightness = settings.StarBrightnessScale > 0f ? settings.StarBrightnessScale : DefaultStarBrightness;
                float starTextureOpacity = settings.StarTextureOpacityScale > 0f ? settings.StarTextureOpacityScale : DefaultStarTextureOpacity;
                float starTextureScale = settings.StarTextureScale > 0f ? settings.StarTextureScale : DefaultStarTextureScale;

                _skyboxMaterial.SetColor(k_SkyboxSkyColorId, ToColor(sky.SkyColorRgb));
                _skyboxMaterial.SetColor(k_SkyboxSunDiscColorId, ToColor(sky.SunDiscColorRgb));
                _skyboxMaterial.SetTexture(k_SkyboxSunTexId, _sunTexture);
                _skyboxMaterial.SetTexture(k_SkyboxSunGlareTexId, _sunGlareTexture);
                _skyboxMaterial.SetTexture(k_SkyboxStarTexId, _starTexture);
                _skyboxMaterial.SetTexture(k_SkyboxMasserTexId, _masserPhaseTextures[masserPhase]);
                _skyboxMaterial.SetTexture(k_SkyboxSecundaTexId, _secundaPhaseTextures[secundaPhase]);
                _skyboxMaterial.SetTexture(k_SkyboxMasserMaskTexId, _masserMaskTexture);
                _skyboxMaterial.SetTexture(k_SkyboxSecundaMaskTexId, _secundaMaskTexture);
                _skyboxMaterial.SetTexture(k_SkyboxCloudTexId, cloudTexture);
                _skyboxMaterial.SetTexture(k_SkyboxNextCloudTexId, nextCloudTexture);
                _skyboxMaterial.SetVector(k_SkyboxSunDirectionId, ToVector4(math.normalizesafe(sky.SkySunWorldDirection, math.up()), sky.Glare));
                _skyboxMaterial.SetVector(k_SkyboxMasserDirectionId, ToVector4(math.normalizesafe(sky.MasserWorldDirection, math.up()), math.radians(math.max(1.4f, sky.MasserSize * MoonAngularDegreesScale))));
                _skyboxMaterial.SetVector(k_SkyboxSecundaDirectionId, ToVector4(math.normalizesafe(sky.SecundaWorldDirection, math.up()), math.radians(math.max(1.1f, sky.SecundaSize * MoonAngularDegreesScale))));
                _skyboxMaterial.SetVector(k_SkyboxMoonWeatherId, new Vector4(masserOpacity, secundaOpacity, sky.MasserShadowBlend, sky.SecundaShadowBlend));
                _skyboxMaterial.SetVector(k_SkyboxMoonPresentationId, new Vector4(moonBrightness, moonEmission, moonMaskScale, 0f));
                _skyboxMaterial.SetVector(k_SkyboxStarPresentationId, new Vector4(starBrightness, starTextureOpacity, starTextureScale, 0f));
                _skyboxMaterial.SetVector(k_SkyboxCloudWeatherId, new Vector4(cloudOpacity, sky.CloudUvOffset, sky.CloudSpeed, sky.WeatherTransition));
                _skyboxMaterial.SetVector(k_SkyboxWeatherId, new Vector4(sky.StarRotationDegrees, exterior ? sky.StarOpacity : 0f, exterior ? sky.SunDiscOpacity : 0f, sky.LightningBrightness));
            }

            MeshRenderer CreatePrimitive(string name, PrimitiveType primitive, Material material, out Transform transform)
            {
                var go = GameObject.CreatePrimitive(primitive);
                go.name = name;
                transform = go.transform;
                transform.SetParent(_rootTransform, false);
                if (go.TryGetComponent<Collider>(out var collider))
                    UnityEngine.Object.Destroy(collider);

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return renderer;
            }

            ParticleSystem CreatePrecipitation(Material material)
            {
                var go = new GameObject("Precipitation");
                var particles = go.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = true;
                main.startLifetime = 1.7f;
                main.startSpeed = 18f;
                main.startSize = 0.055f;
                main.maxParticles = 2400;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = particles.emission;
                emission.rateOverTime = 0f;

                var shape = particles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(70f, 16f, 70f);
                shape.position = new Vector3(0f, 9f, 0f);

                var velocity = particles.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.y = new ParticleSystem.MinMaxCurve(-24f);

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.World;
                renderer.lengthScale = 1f;
                renderer.velocityScale = 0f;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _precipitationRenderer = renderer;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return particles;
            }

            void ApplyBillboard(
                Transform transform,
                Renderer renderer,
                Material material,
                Camera camera,
                float3 direction,
                float size,
                Texture texture,
                Color color,
                float alpha)
            {
                alpha = math.saturate(alpha);
                renderer.enabled = alpha > 0.001f;
                if (!renderer.enabled)
                    return;

                Vector3 worldDirection = ToVector3(math.normalizesafe(direction, math.up()));
                transform.position = camera.transform.position + worldDirection * _skyDistance;
                transform.rotation = Quaternion.LookRotation(camera.transform.position - transform.position, camera.transform.up);
                transform.localScale = new Vector3(size, size, size);
                SetMaterial(material, texture, color, alpha);
            }

            void ApplyMoon(
                Transform transform,
                Renderer renderer,
                Material material,
                Camera camera,
                float3 direction,
                float size,
                int phase,
                float alpha,
                float shadowBlend,
                Color skyColor)
            {
                Texture2D[] phases = material == _secundaMaterial ? _secundaPhaseTextures : _masserPhaseTextures;
                int phaseIndex = math.clamp(phase, 0, phases.Length - 1);
                Color color = Color.Lerp(skyColor, Color.white, math.saturate(shadowBlend));
                float presentationAlpha = alpha > 0.001f ? math.max(MinimumMoonOpacity, math.saturate(alpha)) : 0f;
                ApplyBillboard(transform, renderer, material, camera, direction, math.max(12f, size * MoonScale), phases[phaseIndex], color, presentationAlpha);
            }

            void ApplyPrecipitation(in ActiveSkyWeatherState sky, in MorrowindDayCycleState settings, Camera camera)
            {
                bool active = sky.PrecipitationIntensity > 0.001f;
                if (!active || IsRainPrecipitation(sky.PrecipitationKind))
                {
                    StopPrecipitation();
                    return;
                }

                Material material = _precipitationMaterial;
                if (_precipitationRenderer != null && _precipitationRenderer.sharedMaterial != material)
                    _precipitationRenderer.sharedMaterial = material;
                if (_precipitationRenderer != null)
                {
                    _precipitationRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                    _precipitationRenderer.alignment = ParticleSystemRenderSpace.World;
                    _precipitationRenderer.lengthScale = 1f;
                    _precipitationRenderer.velocityScale = 0f;
                }
                material.mainTexture = ResolvePrecipitationTexture(sky.PrecipitationKind);

                float maxDrops = sky.RainMaxRaindrops > 0 ? sky.RainMaxRaindrops : 950f;
                float entranceSpeed = sky.RainEntranceSpeed > 0f ? sky.RainEntranceSpeed : 7f;
                float targetRate = MorrowindWeatherEvaluationUtility.ResolveOpenMwRainEmissionRate(maxDrops, entranceSpeed);
                float emissionScale = settings.PrecipitationEmissionScale > 0f ? settings.PrecipitationEmissionScale : DefaultPrecipitationEmissionScale;
                float precipitationAmount = math.saturate(sky.PrecipitationIntensity * math.max(0f, sky.PrecipitationAlpha));
                var emission = _precipitation.emission;
                emission.rateOverTime = math.max(80f, targetRate) * precipitationAmount * emissionScale;
                _precipitation.transform.position = camera.transform.position;

                var main = _precipitation.main;
                float worldScale = MorrowindWeatherEvaluationUtility.ResolveRainPresentationWorldScale(settings.PrecipitationWorldScale > 0f ? settings.PrecipitationWorldScale : DefaultPrecipitationWorldScale);
                float particleSizeScale = settings.PrecipitationParticleSizeScale > 0f ? settings.PrecipitationParticleSizeScale : DefaultPrecipitationParticleSizeScale;
                main.startLifetime = 1.7f;
                main.maxParticles = math.max(2400, (int)math.ceil(math.max(maxDrops, targetRate) * 2f));
                main.startSpeed = ResolvePrecipitationSpeed(sky, worldScale);
                main.startSize = new ParticleSystem.MinMaxCurve(ResolvePrecipitationSize(sky, worldScale, particleSizeScale));

                var shape = _precipitation.shape;
                float minHeight = sky.RainMinHeight > 0f ? sky.RainMinHeight * worldScale : 8f;
                float maxHeight = sky.RainMaxHeight > sky.RainMinHeight ? sky.RainMaxHeight * worldScale : minHeight + 12f;
                float vanillaFootprint = sky.RainDiameter > 0f ? sky.RainDiameter * worldScale : 70f;
                float footprint = math.max(12f, vanillaFootprint);
                shape.position = new Vector3(0f, math.lerp(minHeight, maxHeight, 0.5f), 0f);
                shape.scale = new Vector3(footprint, math.max(4f, maxHeight - minHeight), footprint);

                var velocity = _precipitation.velocityOverLifetime;
                float3 storm = math.normalizesafe(sky.StormDirection, new float3(1f, 0f, 0f));
                float lateral = sky.IsStorm != 0 ? math.max(1f, sky.WindSpeed * 0.12f) : 0f;
                float3 resolvedVelocity = new(storm.x * lateral, -ResolvePrecipitationSpeed(sky, worldScale), storm.z * lateral);
                velocity.x = new ParticleSystem.MinMaxCurve(resolvedVelocity.x);
                velocity.y = new ParticleSystem.MinMaxCurve(resolvedVelocity.y);
                velocity.z = new ParticleSystem.MinMaxCurve(resolvedVelocity.z);
                main.startRotation3D = false;
                main.startRotation = 0f;

                if (!_precipitation.isPlaying)
                    _precipitation.Play();
            }

            static float ResolvePrecipitationSpeed(in ActiveSkyWeatherState sky, float worldScale)
            {
                if (sky.RainSpeed > 100f)
                    return math.max(4f, sky.RainSpeed * worldScale);
                return math.max(4f, sky.RainSpeed);
            }

            static float ResolvePrecipitationSize(in ActiveSkyWeatherState sky, float worldScale, float particleSizeScale)
            {
                float baseSize = MorrowindWeatherEvaluationUtility.ResolveRainPresentationSize(worldScale, particleSizeScale);
                WeatherKind kind = (WeatherKind)sky.PrecipitationKind;
                return kind switch
                {
                    WeatherKind.Snow => math.max(0.08f, baseSize * 1.6f),
                    WeatherKind.Blizzard => math.max(0.09f, baseSize * 1.8f),
                    WeatherKind.Ashstorm => math.max(0.12f, baseSize * 2.2f),
                    WeatherKind.Blight => math.max(0.14f, baseSize * 2.4f),
                    _ => math.max(0.025f, baseSize),
                };
            }

            static bool IsRainPrecipitation(int precipitationKind)
            {
                var kind = (WeatherKind)precipitationKind;
                return kind == WeatherKind.Rain || kind == WeatherKind.Thunderstorm;
            }

            void StopPrecipitation()
            {
                if (_precipitation != null && _precipitation.isPlaying)
                    _precipitation.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            Material CreateMaterial(Texture texture, int renderQueue)
                => CreateMaterial(texture, renderQueue, _shader);

            static Material CreateMaterial(Texture texture, int renderQueue, Shader shader)
            {
                var material = new Material(shader)
                {
                    mainTexture = texture,
                    renderQueue = renderQueue,
                };
                SetMaterial(material, texture, Color.white, 1f);
                return material;
            }

            static Shader FindRequiredShader(string shaderName)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                    throw new InvalidOperationException(
                        $"[VVardenfell][SkyWeather] Required shader '{shaderName}' is missing. "
                        + "Add it to GraphicsSettings always-included shaders or keep a serialized reference to it.");
                return shader;
            }

            static void SetMaterial(Material material, Texture texture, Color color, float alpha)
            {
                material.mainTexture = texture;
                material.color = color;
                material.SetColor("_Color", color);
                material.SetFloat("_Alpha", math.saturate(alpha));
            }

            static Texture2D CreateSolidTexture(Color color)
            {
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "VV.SolidWhite" };
                texture.SetPixel(0, 0, color);
                texture.Apply(false, true);
                return texture;
            }

            static Texture2D CreateSunTexture(int size)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "VV.SunDisc" };
                var pixels = new Color[size * size];
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) / center;
                        float dy = (y - center) / center;
                        float radius = math.sqrt(dx * dx + dy * dy);
                        float alpha = math.saturate((1f - radius) * 5f);
                        float glow = math.saturate(1f - radius);
                        pixels[y * size + x] = new Color(1f, 0.96f, 0.82f, alpha * (0.35f + glow * 0.65f));
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply(false, true);
                return texture;
            }

            static Texture2D CreateRainTexture(int size)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "VV.RainDrop" };
                texture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color[size * size];
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = math.abs((x - center) / center);
                        float vertical = math.saturate(1f - math.abs(y - center) / center);
                        float alpha = math.saturate((0.18f - dx) * 8f) * vertical;
                        pixels[y * size + x] = new Color(0.8f, 0.9f, 1f, alpha);
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply(false, true);
                return texture;
            }

            Texture2D ResolveCloudTexture(int weatherIndex)
            {
                if (_cloudTextures == null || _cloudTextures.Length == 0)
                    throw new InvalidOperationException("[VVardenfell][SkyWeather] No baked cloud texture table is loaded.");

                if ((uint)weatherIndex < (uint)_cloudTextures.Length && _cloudTextures[weatherIndex] != null)
                    return _cloudTextures[weatherIndex];

                string weatherId = (uint)weatherIndex < (uint)(_cloudWeatherIds?.Length ?? 0)
                    ? _cloudWeatherIds[weatherIndex]
                    : "<unknown>";
                string path = (uint)weatherIndex < (uint)(_cloudTexturePaths?.Length ?? 0)
                    ? _cloudTexturePaths[weatherIndex]
                    : string.Empty;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(path)
                        ? $"[VVardenfell][SkyWeather] Weather '{weatherId}' at index {weatherIndex} has no cloud texture path."
                        : $"[VVardenfell][SkyWeather] Missing baked cloud texture for weather '{weatherId}' at index {weatherIndex}: {path}. Rebake with data that contains this texture or prevent this weather from being selected.");
            }

            Texture2D ResolveNextCloudTexture(int currentWeather, int nextWeather, float transition, Texture2D currentTexture)
            {
                if (nextWeather == currentWeather || transition <= 0.001f)
                    return currentTexture;
                return ResolveCloudTexture(nextWeather);
            }

            void LoadBakedTexturesOrThrow(VVardenfell.Runtime.Cache.CacheLoader cache, Unity.Entities.BlobAssetReference<RuntimeContentBlob> contentBlob)
            {
                ref RuntimeContentBlob content = ref contentBlob.Value;
                ref RuntimeSkyWeatherVisualSettingsDefBlob visual = ref content.SkyWeatherVisualSettings;
                var missing = new StringBuilder();

                string[] masserPhaseTextures = ToStringArray(ref content.SkyMasserPhaseTextures);
                string[] secundaPhaseTextures = ToStringArray(ref content.SkySecundaPhaseTextures);
                string[] precipitationTextures = ToStringArray(ref content.SkyPrecipitationTextures);

                _sunTexture = LoadRequiredTexture(cache, visual.SunTexture.ToString(), "sun disc", missing);
                _sunGlareTexture = LoadRequiredTexture(cache, visual.SunGlareTexture.ToString(), "sun glare", missing);
                _starTexture = LoadRequiredTexture(cache, visual.StarTexture.ToString(), "stars", missing);
                _masserMaskTexture = LoadRequiredTexture(cache, visual.MasserShadowTexture.ToString(), "Masser mask", missing);
                _secundaMaskTexture = LoadRequiredTexture(cache, visual.SecundaShadowTexture.ToString(), "Secunda mask", missing);
                _masserPhaseTextures = LoadRequiredTextureSet(cache, masserPhaseTextures, "Masser phase", 8, missing);
                _secundaPhaseTextures = LoadRequiredTextureSet(cache, secundaPhaseTextures, "Secunda phase", 8, missing);
                _cloudTextures = LoadCloudTextures(cache, ref content.WeatherDefinitions);
                _precipitationTextures = LoadPrecipitationTextures(cache, precipitationTextures);
                _precipitationTexturePaths = precipitationTextures;
                if (_cloudTextures.Length == 0)
                    missing.AppendLine("- weather cloud texture table: no weather definitions");

                if (missing.Length > 0)
                {
                    throw new InvalidOperationException(
                        "[VVardenfell][SkyWeather] Required OpenMW core sky textures are missing from the runtime cache. "
                        + "Rebake the cache from a Morrowind data install that contains these textures.\n"
                        + missing);
                }
            }

            static Texture2D LoadRequiredTexture(
                VVardenfell.Runtime.Cache.CacheLoader cache,
                string path,
                string label,
                StringBuilder missing)
            {
                if (!string.IsNullOrWhiteSpace(path) && cache.TryGetTextureByPath(path, out var texture))
                    return ConfigureRepeatingTexture(texture);

                missing.Append("- ")
                    .Append(label)
                    .Append(": ")
                    .Append(string.IsNullOrWhiteSpace(path) ? "<empty path>" : path)
                    .AppendLine();
                return null;
            }

            static Texture2D[] LoadRequiredTextureSet(
                VVardenfell.Runtime.Cache.CacheLoader cache,
                string[] paths,
                string label,
                int expectedCount,
                StringBuilder missing)
            {
                if (paths == null || paths.Length < expectedCount)
                {
                    missing.Append("- ")
                        .Append(label)
                        .Append(" texture set: expected ")
                        .Append(expectedCount)
                        .Append(", found ")
                        .Append(paths?.Length ?? 0)
                        .AppendLine();
                    return Array.Empty<Texture2D>();
                }

                var textures = new Texture2D[paths.Length];
                for (int i = 0; i < textures.Length; i++)
                {
                    textures[i] = LoadRequiredTexture(cache, paths[i], $"{label} {i}", missing);
                }

                return textures;
            }

            Texture2D[] LoadPrecipitationTextures(VVardenfell.Runtime.Cache.CacheLoader cache, string[] paths)
            {
                if (paths == null || paths.Length == 0)
                    return Array.Empty<Texture2D>();

                var textures = new Texture2D[paths.Length];
                for (int i = 0; i < paths.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(paths[i]) && cache.TryGetTextureByPath(paths[i], out var texture))
                        textures[i] = ConfigureRepeatingTexture(texture);
                }

                return textures;
            }

            Texture2D ResolvePrecipitationTexture(int precipitationKind)
            {
                int index = PrecipitationTextureIndex((WeatherKind)precipitationKind);
                if (index == 0)
                    return _rainTexture;

                if ((uint)index < (uint)(_precipitationTextures?.Length ?? 0) && _precipitationTextures[index] != null)
                    return _precipitationTextures[index];

                string path = (uint)index < (uint)(_precipitationTexturePaths?.Length ?? 0)
                    ? _precipitationTexturePaths[index]
                    : string.Empty;
                throw new InvalidOperationException(
                    $"[VVardenfell][SkyWeather] Missing selected precipitation texture for weather kind '{(WeatherKind)precipitationKind}' at precipitation index {index}: "
                    + (string.IsNullOrWhiteSpace(path) ? "<empty path>" : path)
                    + ". Rebake with data that contains this texture or prevent this weather from being selected.");
            }

            static int PrecipitationTextureIndex(WeatherKind kind)
            {
                return kind switch
                {
                    WeatherKind.Snow => 1,
                    WeatherKind.Ashstorm => 2,
                    WeatherKind.Blight => 3,
                    WeatherKind.Blizzard => 4,
                    _ => 0,
                };
            }

            Texture2D[] LoadCloudTextures(
                VVardenfell.Runtime.Cache.CacheLoader cache,
                ref Unity.Entities.BlobArray<RuntimeWeatherDefinitionDefBlob> weatherDefinitions)
            {
                _weatherDefinitionCount = weatherDefinitions.Length;
                if (_weatherDefinitionCount <= 0)
                    return Array.Empty<Texture2D>();

                var textures = new Texture2D[_weatherDefinitionCount];
                _cloudTexturePaths = new string[_weatherDefinitionCount];
                _cloudWeatherIds = new string[_weatherDefinitionCount];
                for (int i = 0; i < _weatherDefinitionCount; i++)
                {
                    _cloudWeatherIds[i] = weatherDefinitions[i].Id.ToString();
                    string path = weatherDefinitions[i].CloudTexture.ToString();
                    _cloudTexturePaths[i] = path ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    if (cache.TryGetTextureByPath(path, out var texture))
                        textures[i] = ConfigureRepeatingTexture(texture);
                }

                return textures;
            }

            static string[] ToStringArray(ref Unity.Entities.BlobArray<RuntimeContentStringBlob> values)
            {
                if (values.Length == 0)
                    return Array.Empty<string>();

                var result = new string[values.Length];
                for (int i = 0; i < values.Length; i++)
                    result[i] = values[i].Value.ToString();
                return result;
            }

            static Texture2D ConfigureRepeatingTexture(Texture2D texture)
            {
                if (texture != null)
                    texture.wrapMode = TextureWrapMode.Repeat;
                return texture;
            }

            static Texture2D ConfigureClampedTexture(Texture2D texture)
            {
                if (texture != null)
                    texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }

            Texture2D Own(Texture2D texture)
            {
                if (texture != null)
                    _ownedTextures.Add(texture);
                return texture;
            }

            static void SetUniformScale(Transform transform, float scale) => transform.localScale = new Vector3(scale, scale, scale);

            static Color ToColor(float3 rgb) => new(rgb.x, rgb.y, rgb.z, 1f);

            static Vector3 ToVector3(float3 value) => new(value.x, value.y, value.z);

            static Vector4 ToVector4(float3 value, float w) => new(value.x, value.y, value.z, w);

            static void DestroyObject(UnityEngine.Object obj)
            {
                if (obj == null)
                    return;
                UnityEngine.Object.Destroy(obj);
            }

        }
    }
}
