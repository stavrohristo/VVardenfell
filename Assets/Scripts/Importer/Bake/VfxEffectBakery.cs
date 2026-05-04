using System;
using System.Collections.Generic;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    internal sealed class VfxEffectBakery
    {
        readonly Dictionary<string, int> _effectIndexByModelPath = new(StringComparer.OrdinalIgnoreCase);
        readonly List<MorrowindVfxEffectDef> _effects = new();
        readonly List<MorrowindVfxParticleSystemDef> _particleSystems = new();
        readonly List<MorrowindVfxInitialParticleDef> _initialParticles = new();
        readonly List<MorrowindVfxParticleModifierDef> _modifiers = new();
        readonly List<MorrowindVfxControllerDef> _controllers = new();
        readonly List<string> _texturePaths = new();

        public bool Modified { get; private set; }

        public void GetOrAddModel(string modelPath, NifFile nif, bool required, TextureBakery textures)
        {
            string normalized = ActorVisualContentRules.NormalizeModelPath(modelPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;
            if (_effectIndexByModelPath.ContainsKey(normalized))
                return;

            var extracted = NifVfxEffectExtractor.Extract(nif, normalized, required);
            for (int i = 0; i < extracted.TexturePaths.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(extracted.TexturePaths[i]))
                    textures.AddOrGet(extracted.TexturePaths[i]);
            }

            if (!required
                && extracted.Effect.ControllerCategories == MorrowindVfxControllerCategory.None
                && extracted.ParticleSystems.Count == 0)
            {
                _effectIndexByModelPath[normalized] = -1;
                return;
            }

            extracted.Effect.FirstParticleSystemIndex = extracted.ParticleSystems.Count > 0 ? _particleSystems.Count : -1;
            extracted.Effect.ParticleSystemCount = extracted.ParticleSystems.Count;
            extracted.Effect.FirstControllerIndex = extracted.Controllers.Count > 0 ? _controllers.Count : -1;
            extracted.Effect.ControllerCount = extracted.Controllers.Count;
            extracted.Effect.FirstTexturePathIndex = extracted.TexturePaths.Count > 0 ? _texturePaths.Count : -1;
            extracted.Effect.TexturePathCount = extracted.TexturePaths.Count;

            _effectIndexByModelPath[normalized] = _effects.Count;
            _effects.Add(extracted.Effect);
            _particleSystems.AddRange(extracted.ParticleSystems);
            _initialParticles.AddRange(extracted.InitialParticles);
            _modifiers.AddRange(extracted.Modifiers);
            _controllers.AddRange(extracted.Controllers);
            _texturePaths.AddRange(extracted.TexturePaths);
            Modified = true;
        }

        public MorrowindVfxCatalogData BuildCatalog()
        {
            return new MorrowindVfxCatalogData
            {
                Effects = _effects.ToArray(),
                ParticleSystems = _particleSystems.ToArray(),
                InitialParticles = _initialParticles.ToArray(),
                Modifiers = _modifiers.ToArray(),
                Controllers = _controllers.ToArray(),
                TexturePaths = _texturePaths.ToArray(),
            };
        }
    }
}
