using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Vfx
{
    internal struct MorrowindVfxParticleGpu
    {
        public float3 Position;
        public float Age;
        public float3 Velocity;
        public float Lifetime;
        public float4 Color;
        public float Size;
        public int Bucket;
        public int Slice;
        public int Alive;
        public int InstanceSlot;
        public float RotationAngle;
        public float RotationSpeed;
    }

    public sealed class MorrowindVfxResources : IDisposable
    {
        const string ComputeShaderPath = "MorrowindVfxParticles";
        const string ShaderName = "VVardenfell/MorrowindGpuParticle";
        const string SimulateKernelName = "SimulateParticles";
        const int MaxInstances = 1024;
        const int MaxParticles = 65536;
        const int ThreadsPerGroup = 128;

        static readonly int k_ParticlesId = Shader.PropertyToID("_VfxParticles");
        static readonly int k_ParticleCountId = Shader.PropertyToID("_VfxParticleCount");
        static readonly int k_DeltaTimeId = Shader.PropertyToID("_VfxDeltaTime");
        static readonly int k_BaseArrayId = Shader.PropertyToID("_BaseArray");
        static readonly int k_DrawBucketId = Shader.PropertyToID("_VfxDrawBucket");
        static readonly int k_InstanceDeltasId = Shader.PropertyToID("_VfxInstanceDeltas");
        static readonly int k_InstanceDeltaCountId = Shader.PropertyToID("_VfxInstanceDeltaCount");
        static readonly int k_CameraRightId = Shader.PropertyToID("_VfxCameraRight");
        static readonly int k_CameraUpId = Shader.PropertyToID("_VfxCameraUp");

        readonly List<Instance> _instances = new(MaxInstances);
        readonly MorrowindVfxParticleGpu[] _particleScratch = new MorrowindVfxParticleGpu[MaxParticles];
        readonly float3[] _instanceDeltaScratch = new float3[MaxInstances];
        readonly FollowResult[] _followResultScratch = new FollowResult[MaxInstances];
        readonly bool[] _followResultSetScratch = new bool[MaxInstances];
        readonly bool[] _instanceSlotUsed = new bool[MaxInstances];
        readonly int[] _particleCountByBucket;
        readonly Dictionary<string, int> _effectIndexByModelPath = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<ulong, int> _effectIndexByModelPathHash = new();
        readonly Material[] _materialsByBucket;
        readonly ComputeShader _computeShader;
        readonly int _simulateKernel;
        readonly ComputeBuffer _argsBuffer;
        GraphicsBuffer _particleBuffer;
        GraphicsBuffer _instanceDeltaBuffer;
        bool _particleBufferDirty;
        int _particleCount;
        int _nextInstanceId = 1;
        int _lastSimulatedFrame = -1;

        public int ParticleCount => _particleCount;
        public int InstanceCount => _instances.Count;

        struct Instance
        {
            public int Id;
            public int Slot;
            public int FirstParticle;
            public int ParticleCount;
            public float Age;
            public float Lifetime;
            public byte Loop;
            public uint EffectId;
            public Entity FollowEntity;
            public float3 LastFollowPosition;
            public int CreatedFrame;
        }

        public struct FollowRequest
        {
            public int InstanceIndex;
            public Entity FollowEntity;
        }

        public struct FollowResult
        {
            public int InstanceIndex;
            public byte Found;
            public float3 Position;
        }

        public MorrowindVfxResources(CacheLoader cache)
        {
            _computeShader = Resources.Load<ComputeShader>(ComputeShaderPath);
            if (_computeShader == null)
                throw new InvalidOperationException($"[VVardenfell][VFX] Required compute shader Resources/{ComputeShaderPath}.compute is missing.");
            _simulateKernel = _computeShader.FindKernel(SimulateKernelName);

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException($"[VVardenfell][VFX] Required particle shader '{ShaderName}' is missing.");

            int bucketCount = WorldResources.RefBaseArrays?.Length ?? 0;
            if (bucketCount <= 0)
                throw new InvalidOperationException("[VVardenfell][VFX] Texture array buckets are unavailable.");

            _materialsByBucket = new Material[bucketCount];
            _particleCountByBucket = new int[bucketCount];
            for (int i = 0; i < bucketCount; i++)
            {
                var array = WorldResources.RefBaseArrays[i];
                if (array == null)
                    throw new InvalidOperationException($"[VVardenfell][VFX] Texture array bucket {i} is null.");

                var material = new Material(shader) { name = $"VV VFX Particles Bucket {i}" };
                material.SetTexture(k_BaseArrayId, array);
                material.SetInt(k_DrawBucketId, i);
                _materialsByBucket[i] = material;
            }

            BuildEffectLookup(cache?.VfxCatalog);
            _particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxParticles, Marshal.SizeOf<MorrowindVfxParticleGpu>());
            _instanceDeltaBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxInstances, sizeof(float) * 3);
            _argsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            _argsBuffer.SetData(new uint[] { 0u, 1u, 0u, 0u });
        }

        public void Spawn(CacheLoader cache, in MorrowindVfxSpawnRequest request)
        {
            if (cache?.VfxCatalog?.Effects == null)
                throw new InvalidOperationException("[VVardenfell][VFX] VFX catalog is not loaded.");
            if (!SystemInfo.supportsComputeShaders)
                throw new InvalidOperationException("[VVardenfell][VFX] GPU VFX requires compute shader support.");

            ulong modelPathHash = request.ModelPathHash;
            string modelPath = null;
            int effectIndex;
            if (modelPathHash != 0UL)
            {
                if (!_effectIndexByModelPathHash.TryGetValue(modelPathHash, out effectIndex))
                    throw new InvalidOperationException($"[VVardenfell][VFX] Model path hash 0x{modelPathHash:X16} has no baked VFX entry; rebake required.");
            }
            else
            {
                modelPath = NormalizeModelPath(request.ModelPath.ToString());
                if (string.IsNullOrWhiteSpace(modelPath))
                    throw new InvalidOperationException("[VVardenfell][VFX] Spawn request has no model path hash.");
                if (!_effectIndexByModelPath.TryGetValue(modelPath, out effectIndex))
                    throw new InvalidOperationException($"[VVardenfell][VFX] Model '{modelPath}' has no baked VFX entry; rebake required.");
                modelPathHash = RuntimeContentStableHash.HashPath(modelPath);
            }

            var effect = cache.VfxCatalog.Effects[effectIndex];
            if (effect.UnsupportedRequiredCategories != MorrowindVfxControllerCategory.None)
                throw new InvalidOperationException($"[VVardenfell][VFX] Required VFX model '{modelPath}' has unsupported categories {effect.UnsupportedRequiredCategories}.");
            if (effect.ParticleSystemCount <= 0)
                throw new InvalidOperationException($"[VVardenfell][VFX] Model '{modelPath}' has no baked particle systems for GPU VFX.");
            if (_instances.Count >= MaxInstances)
                throw new InvalidOperationException($"[VVardenfell][VFX] Effect instance capacity {MaxInstances} exceeded.");

            int instanceSlot = AllocateInstanceSlot();
            int firstParticle = _particleCount;
            float requestScale = request.Scale <= 0f ? 1f : request.Scale;
            float4x4 localToWorld = float4x4.TRS(request.Position, request.Rotation, new float3(requestScale));
            for (int s = 0; s < effect.ParticleSystemCount; s++)
            {
                int systemIndex = effect.FirstParticleSystemIndex + s;
                if ((uint)systemIndex >= (uint)cache.VfxCatalog.ParticleSystems.Length)
                    throw new InvalidOperationException($"[VVardenfell][VFX] Model '{modelPath}' particle system index {systemIndex} is invalid.");

                var system = cache.VfxCatalog.ParticleSystems[systemIndex];
                int quota = system.Quota;
                if (quota <= 0)
                    throw new InvalidOperationException($"[VVardenfell][VFX] Model '{modelPath}' particle system '{system.NodeName}' has zero quota.");
                if (_particleCount + quota > MaxParticles)
                    throw new InvalidOperationException($"[VVardenfell][VFX] Particle capacity {MaxParticles} exceeded by '{modelPath}'.");

                ResolveParticleTexture(cache, request.TextureOverridePathHash, request.TextureOverridePath.ToString(), system.TexturePath, modelPathHash, out int bucket, out int slice);
                ResolveParticleRotation(cache.VfxCatalog, system, out bool randomInitialRotation, out float rotationSpeed);
                int emittedParticleIndex = 0;
                for (int p = 0; p < quota; p++)
                {
                    MorrowindVfxInitialParticleDef initial = default;
                    bool hasInitial = p < system.InitialParticleCount
                                      && system.FirstInitialParticleIndex >= 0
                                      && (uint)(system.FirstInitialParticleIndex + p) < (uint)cache.VfxCatalog.InitialParticles.Length;
                    if (hasInitial)
                        initial = cache.VfxCatalog.InitialParticles[system.FirstInitialParticleIndex + p];

                    float3 localPosition;
                    float3 localVelocity;
                    float lifetime;
                    float age;
                    int alive;
                    if (hasInitial)
                    {
                        localPosition = new float3(initial.PositionX, initial.PositionY, initial.PositionZ);
                        localVelocity = new float3(initial.VelocityX, initial.VelocityY, initial.VelocityZ);
                        lifetime = initial.Lifetime > 0f ? initial.Lifetime : math.max(0.01f, system.Lifetime);
                        age = math.max(0f, initial.Age);
                        alive = 1;
                    }
                    else if (system.BirthRate > 0f)
                    {
                        int emissionIndex = emittedParticleIndex++;
                        localPosition = SampleEmitterPosition(system, firstParticle + p);
                        localVelocity = SampleEmitterVelocity(system, firstParticle + p);
                        lifetime = SampleLifetime(system, firstParticle + p);
                        age = -emissionIndex / system.BirthRate;
                        alive = 1;
                    }
                    else
                    {
                        localPosition = float3.zero;
                        localVelocity = float3.zero;
                        lifetime = math.max(0.01f, system.Lifetime);
                        age = 0f;
                        alive = 0;
                    }

                    float3 worldPosition = math.transform(localToWorld, localPosition);
                    float size = (hasInitial && initial.Size > 0f ? initial.Size : math.max(0.01f, system.InitialSize)) * requestScale;

                    if (alive != 0)
                        _particleCountByBucket[bucket]++;

                    _particleScratch[_particleCount++] = new MorrowindVfxParticleGpu
                    {
                        Position = worldPosition,
                        Age = age,
                        Velocity = math.rotate(request.Rotation, localVelocity * requestScale),
                        Lifetime = lifetime,
                        Color = new float4(system.InitialColorR, system.InitialColorG, system.InitialColorB, system.InitialColorA <= 0f ? 1f : system.InitialColorA),
                        Size = size,
                        Bucket = bucket,
                        Slice = slice,
                        Alive = alive,
                        InstanceSlot = instanceSlot,
                        RotationAngle = randomInitialRotation ? DeterministicAngle(firstParticle + p) : 0f,
                        RotationSpeed = rotationSpeed,
                    };
                }
            }

            int particleCount = _particleCount - firstParticle;
            if (particleCount <= 0)
                throw new InvalidOperationException($"[VVardenfell][VFX] Model '{modelPath}' spawned no particles.");

            _instances.Add(new Instance
            {
                Id = _nextInstanceId++,
                Slot = instanceSlot,
                FirstParticle = firstParticle,
                ParticleCount = particleCount,
                Lifetime = effect.Lifetime > 0f ? effect.Lifetime : ResolveParticleLifetime(cache.VfxCatalog, effect),
                Loop = request.Loop,
                EffectId = request.EffectId,
                FollowEntity = request.FollowEntity,
                LastFollowPosition = request.Position,
                CreatedFrame = Time.frameCount,
            });

            _particleBufferDirty = true;
        }

        public void Remove(Unity.Entities.Entity owner, uint effectId)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                var instance = _instances[i];
                if ((effectId == 0u || instance.EffectId == effectId)
                    && (owner == Entity.Null || instance.FollowEntity == owner))
                {
                    RemoveInstanceAt(i);
                }
            }
        }

        public void CollectFollowRequests(NativeList<FollowRequest> requests)
        {
            requests.Clear();
            if (requests.Capacity < _instances.Count)
                requests.Capacity = _instances.Count;

            for (int i = 0; i < _instances.Count; i++)
            {
                var instance = _instances[i];
                if (instance.FollowEntity == Entity.Null)
                    continue;

                requests.AddNoResize(new FollowRequest
                {
                    InstanceIndex = i,
                    FollowEntity = instance.FollowEntity,
                });
            }
        }

        public void Tick(float deltaTime, NativeArray<FollowResult> followResults)
        {
            Array.Clear(_instanceDeltaScratch, 0, _instanceDeltaScratch.Length);
            Array.Clear(_followResultSetScratch, 0, math.min(_instances.Count, _followResultSetScratch.Length));
            for (int i = 0; i < followResults.Length; i++)
            {
                var result = followResults[i];
                if ((uint)result.InstanceIndex >= (uint)_followResultScratch.Length)
                    continue;

                _followResultScratch[result.InstanceIndex] = result;
                _followResultSetScratch[result.InstanceIndex] = true;
            }

            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                var instance = _instances[i];
                if (instance.CreatedFrame == Time.frameCount)
                {
                    _instances[i] = instance;
                    continue;
                }

                instance.Age += deltaTime;
                if (instance.Loop == 0 && instance.Age >= instance.Lifetime)
                {
                    RemoveInstanceAt(i);
                    continue;
                }

                if (instance.Loop != 0 && instance.Lifetime > 0f && instance.Age >= instance.Lifetime)
                    instance.Age = 0f;

                if (instance.FollowEntity != Entity.Null)
                {
                    if (!_followResultSetScratch[i] || _followResultScratch[i].Found == 0)
                    {
                        RemoveInstanceAt(i);
                        continue;
                    }

                    float3 followPosition = _followResultScratch[i].Position;
                    _instanceDeltaScratch[instance.Slot] = followPosition - instance.LastFollowPosition;
                    instance.LastFollowPosition = followPosition;
                }

                _instances[i] = instance;
            }
        }

        public void RecordDispatch(CommandBuffer cmd, Camera camera, bool bindCameraTarget = true)
        {
            if (_particleCount <= 0)
                return;

            if (_particleBufferDirty)
            {
                _particleBuffer.SetData(_particleScratch, 0, 0, _particleCount);
                _argsBuffer.SetData(new uint[] { (uint)(_particleCount * 6), 1u, 0u, 0u });
                _particleBufferDirty = false;
            }
            _instanceDeltaBuffer.SetData(_instanceDeltaScratch);

            int frame = Time.frameCount;
            if (_lastSimulatedFrame != frame)
            {
                _lastSimulatedFrame = frame;
                cmd.SetComputeIntParam(_computeShader, k_ParticleCountId, _particleCount);
                cmd.SetComputeIntParam(_computeShader, k_InstanceDeltaCountId, MaxInstances);
                cmd.SetComputeFloatParam(_computeShader, k_DeltaTimeId, Time.deltaTime);
                cmd.SetComputeBufferParam(_computeShader, _simulateKernel, k_ParticlesId, _particleBuffer);
                cmd.SetComputeBufferParam(_computeShader, _simulateKernel, k_InstanceDeltasId, _instanceDeltaBuffer);
                cmd.DispatchCompute(_computeShader, _simulateKernel, math.max(1, (_particleCount + ThreadsPerGroup - 1) / ThreadsPerGroup), 1, 1);
            }

            if (camera == null)
                throw new InvalidOperationException("[VVardenfell][VFX] Particle render dispatch requires a camera.");
            if (bindCameraTarget)
                cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
            Vector3 cameraRight = camera.transform.right;
            Vector3 cameraUp = camera.transform.up;
            for (int i = 0; i < _materialsByBucket.Length; i++)
            {
                var material = _materialsByBucket[i];
                if (material == null || _particleCountByBucket[i] <= 0)
                    continue;

                material.SetBuffer(k_ParticlesId, _particleBuffer);
                material.SetInt(k_ParticleCountId, _particleCount);
                material.SetInt(k_DrawBucketId, i);
                material.SetVector(k_CameraRightId, cameraRight);
                material.SetVector(k_CameraUpId, cameraUp);
                cmd.DrawProceduralIndirect(Matrix4x4.identity, material, 0, MeshTopology.Triangles, _argsBuffer, 0);
            }
        }

        public void Dispose()
        {
            _particleBuffer?.Release();
            _particleBuffer = null;
            _instanceDeltaBuffer?.Release();
            _instanceDeltaBuffer = null;
            _argsBuffer?.Release();
            if (_materialsByBucket != null)
            {
                for (int i = 0; i < _materialsByBucket.Length; i++)
                {
                    if (_materialsByBucket[i] != null)
                        UnityEngine.Object.Destroy(_materialsByBucket[i]);
                }
            }
        }

        void BuildEffectLookup(MorrowindVfxCatalogData catalog)
        {
            var effects = catalog?.Effects ?? Array.Empty<MorrowindVfxEffectDef>();
            for (int i = 0; i < effects.Length; i++)
            {
                string modelPath = NormalizeModelPath(effects[i]?.ModelPath);
                if (string.IsNullOrWhiteSpace(modelPath))
                    continue;
                if (!_effectIndexByModelPath.ContainsKey(modelPath))
                    _effectIndexByModelPath.Add(modelPath, i);
                ulong hash = RuntimeContentStableHash.HashPath(modelPath);
                if (hash != 0UL && !_effectIndexByModelPathHash.ContainsKey(hash))
                    _effectIndexByModelPathHash.Add(hash, i);
            }
        }

        void ResolveParticleTexture(
            CacheLoader cache,
            ulong overridePathHash,
            string overridePath,
            string bakedPath,
            ulong modelPathHash,
            out int bucket,
            out int slice)
        {
            int textureIndex;
            ulong texturePathHash = overridePathHash != 0UL ? overridePathHash : RuntimeContentStableHash.HashPath(bakedPath);
            if (texturePathHash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][VFX] Model path hash 0x{modelPathHash:X16} particle system has no texture path.");
            if (!cache.TryGetTextureIndexByPathHash(texturePathHash, out textureIndex))
            {
                string texturePath = !string.IsNullOrWhiteSpace(overridePath) ? overridePath : bakedPath;
                if (string.IsNullOrWhiteSpace(texturePath) || !cache.TryGetTextureIndexByPath(texturePath, out textureIndex))
                    throw new InvalidOperationException($"[VVardenfell][VFX] Texture hash 0x{texturePathHash:X16} for model hash 0x{modelPathHash:X16} is missing from texture cache; rebake required.");
            }
            if (!WorldResources.TexBucketInfo.IsCreated || (uint)textureIndex >= (uint)WorldResources.TexBucketInfo.Length)
                throw new InvalidOperationException($"[VVardenfell][VFX] Texture hash 0x{texturePathHash:X16} for model hash 0x{modelPathHash:X16} has no texture bucket.");

            int2 bucketSlice = WorldResources.TexBucketInfo[textureIndex];
            bucket = bucketSlice.x;
            slice = bucketSlice.y;
        }

        static float ResolveParticleLifetime(MorrowindVfxCatalogData catalog, MorrowindVfxEffectDef effect)
        {
            float lifetime = 0f;
            for (int i = 0; i < effect.ParticleSystemCount; i++)
            {
                int index = effect.FirstParticleSystemIndex + i;
                if ((uint)index < (uint)catalog.ParticleSystems.Length)
                    lifetime = math.max(lifetime, catalog.ParticleSystems[index].Lifetime + catalog.ParticleSystems[index].EmitStopTime);
            }

            if (lifetime <= 0f)
                throw new InvalidOperationException($"[VVardenfell][VFX] VFX model '{effect.ModelPath}' has no positive lifetime.");
            return lifetime;
        }

        static void ResolveParticleRotation(
            MorrowindVfxCatalogData catalog,
            MorrowindVfxParticleSystemDef system,
            out bool randomInitialRotation,
            out float rotationSpeed)
        {
            randomInitialRotation = false;
            rotationSpeed = 0f;
            if (system.ModifierCount <= 0 || system.FirstModifierIndex < 0)
                return;

            for (int i = 0; i < system.ModifierCount; i++)
            {
                int index = system.FirstModifierIndex + i;
                if ((uint)index >= (uint)catalog.Modifiers.Length)
                    throw new InvalidOperationException($"[VVardenfell][VFX] Particle system '{system.NodeName}' modifier index {index} is invalid.");

                var modifier = catalog.Modifiers[index];
                if (modifier?.Kind != MorrowindVfxParticleModifierKind.ParticleRotation)
                    continue;

                randomInitialRotation = modifier.X0 != 0f;
                rotationSpeed = modifier.W1;
                return;
            }
        }

        static float DeterministicAngle(int seed)
        {
            float value = math.frac(math.sin((seed + 1) * 12.9898f) * 43758.5453f);
            return value * math.PI * 2f;
        }

        static float Deterministic01(int seed)
            => math.frac(math.sin((seed + 1) * 78.233f) * 43758.5453f);

        static float DeterministicSigned(int seed)
            => Deterministic01(seed) * 2f - 1f;

        static float3 SampleEmitterPosition(MorrowindVfxParticleSystemDef system, int seed)
        {
            return new float3(
                DeterministicSigned(seed + 17) * system.EmitterDimX * 0.5f,
                DeterministicSigned(seed + 31) * system.EmitterDimY * 0.5f,
                DeterministicSigned(seed + 47) * system.EmitterDimZ * 0.5f);
        }

        static float3 SampleEmitterVelocity(MorrowindVfxParticleSystemDef system, int seed)
        {
            float hdir = system.PlanarAngle + system.PlanarAngleVariation * DeterministicSigned(seed + 61);
            float vdir = system.Declination + system.DeclinationVariation * DeterministicSigned(seed + 79);
            quaternion horizontal = quaternion.AxisAngle(new float3(0f, 0f, 1f), hdir);
            quaternion vertical = quaternion.AxisAngle(new float3(0f, 1f, 0f), vdir);
            float3 nifDirection = math.mul(vertical, math.mul(horizontal, new float3(0f, 0f, 1f)));
            float3 localDirection = new(nifDirection.x, nifDirection.z, nifDirection.y);
            float minSpeed = system.Speed - system.SpeedVariation * 0.5f;
            float maxSpeed = system.Speed + system.SpeedVariation * 0.5f;
            float speed = math.max(0f, math.lerp(minSpeed, maxSpeed, Deterministic01(seed + 97)));
            return localDirection * speed;
        }

        static float SampleLifetime(MorrowindVfxParticleSystemDef system, int seed)
            => math.max(0.01f, system.Lifetime + system.LifetimeVariation * Deterministic01(seed + 113));

        void KillParticles(Instance instance)
        {
            for (int i = 0; i < instance.ParticleCount; i++)
            {
                int index = instance.FirstParticle + i;
                if ((uint)index < (uint)_particleScratch.Length)
                    _particleScratch[index].Alive = 0;
            }

            _particleBufferDirty = true;
        }

        void RemoveInstanceAt(int index)
        {
            var instance = _instances[index];
            if ((uint)instance.Slot < (uint)_instanceSlotUsed.Length)
                _instanceSlotUsed[instance.Slot] = false;
            _instances.RemoveAt(index);

            for (int i = 0; i < instance.ParticleCount; i++)
            {
                int particleIndex = instance.FirstParticle + i;
                if ((uint)particleIndex >= (uint)_particleScratch.Length)
                    continue;

                ref var particle = ref _particleScratch[particleIndex];
                if (particle.Alive != 0 && (uint)particle.Bucket < (uint)_particleCountByBucket.Length)
                    _particleCountByBucket[particle.Bucket] = math.max(0, _particleCountByBucket[particle.Bucket] - 1);
            }

            int tailStart = instance.FirstParticle + instance.ParticleCount;
            int tailCount = _particleCount - tailStart;
            if (tailCount > 0)
                Array.Copy(_particleScratch, tailStart, _particleScratch, instance.FirstParticle, tailCount);

            _particleCount -= instance.ParticleCount;
            for (int i = 0; i < _instances.Count; i++)
            {
                var other = _instances[i];
                if (other.FirstParticle > instance.FirstParticle)
                {
                    other.FirstParticle -= instance.ParticleCount;
                    _instances[i] = other;
                }
            }

            Array.Clear(_particleScratch, _particleCount, instance.ParticleCount);
            _argsBuffer.SetData(new uint[] { (uint)(_particleCount * 6), 1u, 0u, 0u });
            _particleBufferDirty = true;
        }

        int AllocateInstanceSlot()
        {
            for (int i = 0; i < _instanceSlotUsed.Length; i++)
            {
                if (_instanceSlotUsed[i])
                    continue;

                _instanceSlotUsed[i] = true;
                return i;
            }

            throw new InvalidOperationException($"[VVardenfell][VFX] Effect instance slot capacity {MaxInstances} exceeded.");
        }

        static string NormalizeModelPath(string modelPath)
            => ActorVisualContentRules.NormalizeModelPath(modelPath);
    }
}
