using System;
using System.Collections.Generic;
using VVardenfell.Core;
using UnityEngine;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Nif
{
    public sealed class NifVfxEffectExtraction
    {
        public MorrowindVfxEffectDef Effect;
        public readonly List<MorrowindVfxParticleSystemDef> ParticleSystems = new();
        public readonly List<MorrowindVfxInitialParticleDef> InitialParticles = new();
        public readonly List<MorrowindVfxParticleModifierDef> Modifiers = new();
        public readonly List<MorrowindVfxControllerDef> Controllers = new();
        public readonly List<string> TexturePaths = new();
    }

    public static class NifVfxEffectExtractor
    {
        static readonly MorrowindVfxControllerCategory UnsupportedRequiredCategories =
            MorrowindVfxControllerCategory.Unknown;

        public static NifVfxEffectExtraction Extract(NifFile nif, string modelPath, bool required)
        {
            if (nif?.Records == null)
                throw new InvalidOperationException($"Cannot extract VFX from null NIF for '{modelPath}'.");

            var result = new NifVfxEffectExtraction
            {
                Effect = new MorrowindVfxEffectDef
                {
                    ModelPath = modelPath ?? string.Empty,
                    Required = required ? (byte)1 : (byte)0,
                    Lifetime = NifEffectControllerAnalysis.ResolveMaxControllerStopTime(nif),
                },
            };

            var textureSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is NiSourceTexture texture && !string.IsNullOrWhiteSpace(texture.FileName))
                    AddTexture(result, textureSet, texture.FileName);
            }

            int[] parentByRecord = BuildParentIndex(nif);
            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is NiTimeController controller)
                    AddController(nif, result, controller, i);

                if (nif.Records[i] is NiParticles particles)
                    AddParticleSystem(nif, result, particles, i, parentByRecord, required);
            }

            var unsupported = result.Effect.ControllerCategories & UnsupportedRequiredCategories;
            result.Effect.UnsupportedRequiredCategories = required ? unsupported : MorrowindVfxControllerCategory.None;
            if (required && unsupported != MorrowindVfxControllerCategory.None)
            {
                throw new InvalidOperationException(
                    $"Required VFX model '{modelPath}' uses unsupported controller categories {unsupported}; GPU VFX support must be implemented before this effect can be required.");
            }

            if (required
                && result.Effect.ControllerCategories != MorrowindVfxControllerCategory.None
                && result.Effect.Lifetime <= 0f)
            {
                throw new InvalidOperationException($"Required VFX model '{modelPath}' has controllers but no positive max controller lifetime.");
            }

            return result;
        }

        public static MorrowindVfxControllerCategory ClassifyController(NifRecord record)
        {
            return record switch
            {
                NiParticleSystemController => MorrowindVfxControllerCategory.Particle,
                NiAlphaController => MorrowindVfxControllerCategory.Alpha,
                NiUVController => MorrowindVfxControllerCategory.Uv,
                NiFlipController => MorrowindVfxControllerCategory.Flip,
                NiMaterialColorController => MorrowindVfxControllerCategory.MaterialColor,
                NiPathController => MorrowindVfxControllerCategory.Path,
                NiGeomMorpherController => MorrowindVfxControllerCategory.Morph,
                NiKeyframeController => MorrowindVfxControllerCategory.Keyframe,
                NiVisController => MorrowindVfxControllerCategory.Visibility,
                _ => record is NiTimeController
                    ? MorrowindVfxControllerCategory.Unknown
                    : MorrowindVfxControllerCategory.None,
            };
        }

        public static bool IsVfxSupportedObjectController(NifRecord record)
        {
            MorrowindVfxControllerCategory category = ClassifyController(record);
            return category is MorrowindVfxControllerCategory.Particle
                or MorrowindVfxControllerCategory.Alpha
                or MorrowindVfxControllerCategory.Uv
                or MorrowindVfxControllerCategory.Flip
                or MorrowindVfxControllerCategory.MaterialColor
                or MorrowindVfxControllerCategory.Morph
                or MorrowindVfxControllerCategory.Path;
        }

        static void AddController(
            NifFile nif,
            NifVfxEffectExtraction result,
            NiTimeController controller,
            int recordIndex)
        {
            MorrowindVfxControllerCategory category = ClassifyController(controller);
            if (category == MorrowindVfxControllerCategory.None)
                return;

            if (float.IsNaN(controller.TimeStop) || float.IsInfinity(controller.TimeStop))
                throw new InvalidOperationException($"NIF '{nif.Path}' controller {recordIndex} has non-finite stop time {controller.TimeStop}.");

            result.Effect.ControllerCategories |= category;
            if (category == MorrowindVfxControllerCategory.Morph)
                ValidateMorphController(nif, controller, recordIndex);

            result.Controllers.Add(new MorrowindVfxControllerDef
            {
                Category = category,
                TargetName = ResolveName(nif, controller.Target),
                Frequency = controller.Frequency,
                Phase = controller.Phase,
                TimeStart = controller.TimeStart,
                TimeStop = controller.TimeStop,
            });
        }

        static void ValidateMorphController(NifFile nif, NiTimeController controller, int recordIndex)
        {
            if (controller is not NiGeomMorpherController morphController)
                throw new InvalidOperationException($"NIF '{nif.Path}' controller {recordIndex} classified as morph but is {controller.GetType().Name}.");
            if (!TryResolve(nif, morphController.Data, out NiMorphData morphData))
                throw new InvalidOperationException($"NIF '{nif.Path}' NiGeomMorpherController {recordIndex} has no NiMorphData.");
            if (morphData.Morphs == null || morphData.Morphs.Length == 0)
                throw new InvalidOperationException($"NIF '{nif.Path}' NiGeomMorpherController {recordIndex} has empty NiMorphData.");
            if (morphData.NumVertices == 0)
                throw new InvalidOperationException($"NIF '{nif.Path}' NiGeomMorpherController {recordIndex} has zero morph vertices.");

            for (int i = 0; i < morphData.Morphs.Length; i++)
            {
                var morph = morphData.Morphs[i];
                if (morph?.Vertices == null || morph.Vertices.Length != morphData.NumVertices)
                {
                    throw new InvalidOperationException(
                        $"NIF '{nif.Path}' NiGeomMorpherController {recordIndex} morph {i} vertex count does not match declared count {morphData.NumVertices}.");
                }

                var keys = morph.Keys?.Keys ?? Array.Empty<NifAnimationKey>();
                for (int k = 0; k < keys.Length; k++)
                {
                    if (!IsFinite(keys[k].Time) || !IsFinite(keys[k].Value.x))
                    {
                        throw new InvalidOperationException(
                            $"NIF '{nif.Path}' NiGeomMorpherController {recordIndex} morph {i} key {k} has non-finite time or weight.");
                    }
                }
            }
        }

        static void AddParticleSystem(
            NifFile nif,
            NifVfxEffectExtraction result,
            NiParticles particles,
            int recordIndex,
            int[] parentByRecord,
            bool required)
        {
            if (!TryResolve(nif, particles.Controller, out NiParticleSystemController controller))
                return;
            if (!TryResolve(nif, particles.Data, out NiParticlesData data))
                throw new InvalidOperationException($"NIF '{nif.Path}' particle node {recordIndex} has no NiParticlesData.");

            Matrix4x4 sourceWorld = BuildSourceWorldMatrix(nif, recordIndex, parentByRecord);
            float sourceScale = ResolveUniformScale(sourceWorld);
            var system = new MorrowindVfxParticleSystemDef
            {
                NodeName = particles.Name ?? string.Empty,
                EmitterNodeName = ResolveName(nif, controller.Emitter),
                TexturePath = ResolveGeometryTexture(nif, particles),
                Quota = controller.NumParticles != 0 ? controller.NumParticles : data.NumParticles,
                ActiveCount = controller.NumValid != 0 ? controller.NumValid : data.ActiveCount,
                EmitFlags = controller.EmitFlags,
                Speed = ConvertDistance(controller.Speed * sourceScale),
                SpeedVariation = ConvertDistance(controller.SpeedVariation * sourceScale),
                Declination = controller.Declination,
                DeclinationVariation = controller.DeclinationVariation,
                PlanarAngle = controller.PlanarAngle,
                PlanarAngleVariation = controller.PlanarAngleVariation,
                InitialSize = ConvertDistance(controller.InitialSize * sourceScale),
                BirthRate = controller.BirthRate,
                Lifetime = controller.Lifetime,
                LifetimeVariation = controller.LifetimeVariation,
                EmitStartTime = controller.EmitStartTime,
                EmitStopTime = controller.EmitStopTime,
                InitialNormalX = ConvertDirection(controller.InitialNormal, sourceWorld).x,
                InitialNormalY = ConvertDirection(controller.InitialNormal, sourceWorld).y,
                InitialNormalZ = ConvertDirection(controller.InitialNormal, sourceWorld).z,
                InitialColorR = controller.InitialColor.x,
                InitialColorG = controller.InitialColor.y,
                InitialColorB = controller.InitialColor.z,
                InitialColorA = controller.InitialColor.w,
                EmitterDimX = ConvertVector(controller.EmitterDimensions, sourceWorld).x,
                EmitterDimY = ConvertVector(controller.EmitterDimensions, sourceWorld).y,
                EmitterDimZ = ConvertVector(controller.EmitterDimensions, sourceWorld).z,
            };

            int firstInitial = result.InitialParticles.Count;
            int activeLimit = system.ActiveCount > 0 ? system.ActiveCount : data.ActiveCount;
            int sourceParticleCount = Math.Min(controller.Particles?.Length ?? 0, activeLimit);
            for (int i = 0; i < sourceParticleCount; i++)
            {
                var particle = controller.Particles[i];
                if (particle.Lifespan <= 0f)
                    continue;
                if (particle.Code >= data.Vertices.Length)
                    continue;

                int particleCode = particle.Code;
                Vector3 position = ConvertPosition(data.Vertices[particleCode], sourceWorld);
                Vector3 velocity = ConvertVector(particle.Velocity, sourceWorld);
                float size = controller.InitialSize * sourceScale;
                if (data.Sizes != null && particleCode < data.Sizes.Length)
                    size *= data.Sizes[particleCode];
                size = ConvertDistance(size);

                result.InitialParticles.Add(new MorrowindVfxInitialParticleDef
                {
                    PositionX = position.x,
                    PositionY = position.y,
                    PositionZ = position.z,
                    VelocityX = velocity.x,
                    VelocityY = velocity.y,
                    VelocityZ = velocity.z,
                    Size = size,
                    Age = particle.Age,
                    Lifetime = particle.Lifespan,
                    SpawnGeneration = particle.SpawnGeneration,
                    Code = particle.Code,
                });
            }

            int initialCount = result.InitialParticles.Count - firstInitial;
            system.FirstInitialParticleIndex = initialCount > 0 ? firstInitial : -1;
            system.InitialParticleCount = initialCount;

            int firstModifier = result.Modifiers.Count;
            AddModifierChain(nif, result, result.Modifiers, controller.Modifier, sourceWorld, sourceScale, required);
            AddModifierChain(nif, result, result.Modifiers, controller.Collider, sourceWorld, sourceScale, required);
            int modifierCount = result.Modifiers.Count - firstModifier;
            system.FirstModifierIndex = modifierCount > 0 ? firstModifier : -1;
            system.ModifierCount = modifierCount;

            result.ParticleSystems.Add(system);
        }

        static void AddModifierChain(
            NifFile nif,
            NifVfxEffectExtraction result,
            List<MorrowindVfxParticleModifierDef> modifiers,
            int modifierIndex,
            Matrix4x4 sourceWorld,
            float sourceScale,
            bool required)
        {
            var visited = new HashSet<int>();
            int current = modifierIndex;
            while (TryResolve(nif, current, out NiParticleModifier modifier))
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException($"NIF '{nif.Path}' particle modifier chain has a cycle at record {current}.");

                if (modifier is NiParticleRotation)
                    result.Effect.ControllerCategories |= MorrowindVfxControllerCategory.ParticleRotation;

                modifiers.Add(ConvertModifier(modifier, sourceWorld, sourceScale));
                current = modifier.Next;
            }
        }

        static MorrowindVfxParticleModifierDef ConvertModifier(NiParticleModifier modifier, Matrix4x4 sourceWorld, float sourceScale)
        {
            switch (modifier)
            {
                case NiParticleGrowFade growFade:
                    return new MorrowindVfxParticleModifierDef
                    {
                        Kind = MorrowindVfxParticleModifierKind.GrowFade,
                        X0 = growFade.GrowTime,
                        Y0 = growFade.FadeTime,
                    };
                case NiParticleColorModifier color:
                    return new MorrowindVfxParticleModifierDef
                    {
                        Kind = MorrowindVfxParticleModifierKind.Color,
                        X0 = color.Data,
                    };
                case NiGravity gravity:
                {
                    Vector3 position = ConvertPosition(gravity.Position, sourceWorld);
                    Vector3 direction = ConvertDirection(gravity.Direction, sourceWorld);
                    return new MorrowindVfxParticleModifierDef
                    {
                        Kind = MorrowindVfxParticleModifierKind.Gravity,
                        X0 = position.x,
                        Y0 = position.y,
                        Z0 = position.z,
                        W0 = ConvertDistance(gravity.Force * sourceScale),
                        X1 = direction.x,
                        Y1 = direction.y,
                        Z1 = direction.z,
                        W1 = gravity.Type,
                    };
                }
                case NiParticleBomb bomb:
                {
                    Vector3 position = ConvertPosition(bomb.Position, sourceWorld);
                    return new MorrowindVfxParticleModifierDef
                    {
                        Kind = MorrowindVfxParticleModifierKind.Bomb,
                        X0 = position.x,
                        Y0 = position.y,
                        Z0 = position.z,
                        W0 = bomb.Strength,
                        X1 = ConvertDistance(bomb.Range * sourceScale),
                        Y1 = bomb.Duration,
                        Z1 = bomb.StartTime,
                        W1 = bomb.DecayType,
                    };
                }
                case NiPlanarCollider planar:
                {
                    Vector3 position = ConvertPosition(planar.Position, sourceWorld);
                    Vector3 normal = ConvertDirection(planar.PlaneNormal, sourceWorld);
                    return new MorrowindVfxParticleModifierDef
                    {
                        Kind = MorrowindVfxParticleModifierKind.PlanarCollider,
                        X0 = position.x,
                        Y0 = position.y,
                        Z0 = position.z,
                        W0 = planar.BounceFactor,
                        X1 = normal.x,
                        Y1 = normal.y,
                        Z1 = normal.z,
                        W1 = ConvertDistance(planar.PlaneDistance * sourceScale),
                    };
                }
                case NiSphericalCollider spherical:
                {
                    Vector3 center = ConvertPosition(spherical.Center, sourceWorld);
                    return new MorrowindVfxParticleModifierDef
                    {
                        Kind = MorrowindVfxParticleModifierKind.SphericalCollider,
                        X0 = center.x,
                        Y0 = center.y,
                        Z0 = center.z,
                        W0 = ConvertDistance(spherical.Radius * sourceScale),
                        X1 = spherical.BounceFactor,
                    };
                }
                case NiParticleRotation rotation:
                {
                    Vector3 initialAxis = ConvertDirection(rotation.InitialAxis, sourceWorld);
                    return new MorrowindVfxParticleModifierDef
                    {
                        Kind = MorrowindVfxParticleModifierKind.ParticleRotation,
                        X0 = rotation.RandomInitialAxis,
                        X1 = initialAxis.x,
                        Y1 = initialAxis.y,
                        Z1 = initialAxis.z,
                        W1 = rotation.RotationSpeed,
                    };
                }
                default:
                    return new MorrowindVfxParticleModifierDef();
            }
        }

        static string ResolveGeometryTexture(NifFile nif, NiGeometry geometry)
        {
            if (geometry.PropertyLinks == null)
                return string.Empty;

            for (int i = 0; i < geometry.PropertyLinks.Length; i++)
            {
                if (!TryResolve(nif, geometry.PropertyLinks[i], out NiTexturingProperty textureProperty)
                    || textureProperty.Textures == null
                    || textureProperty.Textures.Length == 0
                    || textureProperty.Textures[0] == null
                    || !textureProperty.Textures[0].Enabled)
                    continue;

                if (TryResolve(nif, textureProperty.Textures[0].SourceTexture, out NiSourceTexture source)
                    && !string.IsNullOrWhiteSpace(source.FileName))
                    return source.FileName;
            }

            return string.Empty;
        }

        static void AddTexture(NifVfxEffectExtraction result, HashSet<string> textureSet, string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
                return;

            if (textureSet.Add(texturePath))
                result.TexturePaths.Add(texturePath);
        }

        static string ResolveName(NifFile nif, int index)
        {
            return TryResolve(nif, index, out NiObjectNET obj)
                ? obj.Name ?? string.Empty
                : string.Empty;
        }

        static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        static int[] BuildParentIndex(NifFile nif)
        {
            var parents = new int[nif.Records.Length];
            Array.Fill(parents, -1);
            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is not NiNode node || node.Children == null)
                    continue;

                for (int c = 0; c < node.Children.Length; c++)
                {
                    int child = node.Children[c];
                    if ((uint)child < (uint)parents.Length)
                        parents[child] = i;
                }
            }

            return parents;
        }

        static Matrix4x4 BuildSourceWorldMatrix(NifFile nif, int recordIndex, int[] parentByRecord)
        {
            Matrix4x4 world = Matrix4x4.identity;
            var chain = new List<int>();
            for (int current = recordIndex; current >= 0 && current < nif.Records.Length; current = parentByRecord[current])
                chain.Add(current);

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (nif.Records[chain[i]] is NiAVObject obj)
                    world *= BuildSourceLocalMatrix(obj);
            }

            return world;
        }

        static Matrix4x4 BuildSourceLocalMatrix(NiAVObject obj)
        {
            var r = obj.Rotation;
            float s = obj.Scale;
            var local = new Matrix4x4();
            local.m00 = r.m00 * s; local.m01 = r.m01 * s; local.m02 = r.m02 * s; local.m03 = obj.Translation.x;
            local.m10 = r.m10 * s; local.m11 = r.m11 * s; local.m12 = r.m12 * s; local.m13 = obj.Translation.y;
            local.m20 = r.m20 * s; local.m21 = r.m21 * s; local.m22 = r.m22 * s; local.m23 = obj.Translation.z;
            local.m30 = 0f;        local.m31 = 0f;        local.m32 = 0f;        local.m33 = 1f;
            return local;
        }

        static float ResolveUniformScale(Matrix4x4 sourceWorld)
        {
            float sx = new Vector3(sourceWorld.m00, sourceWorld.m10, sourceWorld.m20).magnitude;
            float sy = new Vector3(sourceWorld.m01, sourceWorld.m11, sourceWorld.m21).magnitude;
            float sz = new Vector3(sourceWorld.m02, sourceWorld.m12, sourceWorld.m22).magnitude;
            return Math.Max(0.0001f, (sx + sy + sz) / 3f);
        }

        static Vector3 ConvertPosition(Vector3 value, Matrix4x4 sourceWorld)
        {
            Vector3 transformed = sourceWorld.MultiplyPoint3x4(value);
            return new Vector3(transformed.x, transformed.z, transformed.y) * WorldScale.MwUnitsToMeters;
        }

        static Vector3 ConvertVector(Vector3 value, Matrix4x4 sourceWorld)
        {
            Vector3 transformed = sourceWorld.MultiplyVector(value);
            return new Vector3(transformed.x, transformed.z, transformed.y) * WorldScale.MwUnitsToMeters;
        }

        static Vector3 ConvertDirection(Vector3 value, Matrix4x4 sourceWorld)
        {
            Vector3 transformed = sourceWorld.MultiplyVector(value);
            var converted = new Vector3(transformed.x, transformed.z, transformed.y);
            return converted.sqrMagnitude > 0f ? converted.normalized : Vector3.zero;
        }

        static float ConvertDistance(float value)
            => value * WorldScale.MwUnitsToMeters;

        static bool TryResolve<T>(NifFile nif, int index, out T value) where T : NifRecord
        {
            value = null;
            if (nif?.Records == null || index < 0 || index >= nif.Records.Length)
                return false;

            value = nif.Records[index] as T;
            return value != null;
        }
    }
}
