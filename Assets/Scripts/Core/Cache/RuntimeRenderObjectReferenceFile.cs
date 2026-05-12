using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VVardenfell.Core.Cache
{
    public enum RuntimeRenderObjectReferenceKind : int
    {
        Mesh = 1,
        RefMaterial = 2,
        CombinedMaterial = 3,
        TerrainMaterial = 4,
    }

    public readonly struct RuntimeRenderObjectReference
    {
        public readonly RuntimeRenderObjectReferenceKind Kind;
        public readonly int Index;

        public RuntimeRenderObjectReference(RuntimeRenderObjectReferenceKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }
    }

    public static class RuntimeRenderObjectReferenceFile
    {
        const uint Magic = 0x4F525656u; // 'VVRO'
        const uint Version = 1u;

        public static void WriteWrappedEntityWorld(
            string path,
            byte[] entityBytes,
            object[] referencedObjects,
            IReadOnlyDictionary<Object, RuntimeRenderObjectReference> referencesByObject)
        {
            if (entityBytes == null)
                throw new InvalidDataException($"Cannot write '{path}' without entity bytes.");
            if (referencedObjects == null)
                referencedObjects = System.Array.Empty<object>();
            if (referencesByObject == null)
                throw new InvalidDataException($"Cannot write '{path}' without render object reference metadata.");

            using var fs = File.Create(path);
            using var writer = new BinaryWriter(fs);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(referencedObjects.Length);
            for (int i = 0; i < referencedObjects.Length; i++)
            {
                if (referencedObjects[i] is not Object unityObject)
                    throw new InvalidDataException($"Serialized entity world '{path}' referenced non-Unity object at slot {i}.");
                if (!referencesByObject.TryGetValue(unityObject, out var reference))
                    throw new InvalidDataException($"Serialized entity world '{path}' referenced unmanaged Unity object '{unityObject.name}' at slot {i}.");
                writer.Write((int)reference.Kind);
                writer.Write(reference.Index);
            }

            writer.Write(entityBytes.Length);
            writer.Write(entityBytes);
        }

        public static void ReadWrappedEntityWorld(string path, out RuntimeRenderObjectReference[] references, out byte[] entityBytes)
        {
            ReadWrappedEntityWorldHeader(path, out references, out long payloadOffset, out int byteCount);
            using var fs = File.OpenRead(path);
            fs.Position = payloadOffset;
            if (byteCount <= 0 || byteCount > fs.Length - fs.Position)
                throw new InvalidDataException($"Runtime entity world '{path}' has invalid entity payload length {byteCount}.");
            using var reader = new BinaryReader(fs);
            entityBytes = reader.ReadBytes(byteCount);
            if (entityBytes.Length != byteCount)
                throw new EndOfStreamException($"Runtime entity world '{path}' ended before entity payload was fully read.");
        }

        public static void ReadWrappedEntityWorldHeader(
            string path,
            out RuntimeRenderObjectReference[] references,
            out long entityPayloadOffset,
            out int entityPayloadLength)
        {
            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs);
            uint magic = reader.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Runtime entity world '{path}' has invalid render object table magic 0x{magic:X8}; rebake required.");
            uint version = reader.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Runtime entity world '{path}' has render object table version {version}; expected {Version}; rebake required.");

            int count = reader.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"Runtime entity world '{path}' has negative render object reference count {count}.");
            references = new RuntimeRenderObjectReference[count];
            for (int i = 0; i < count; i++)
            {
                var kind = (RuntimeRenderObjectReferenceKind)reader.ReadInt32();
                int index = reader.ReadInt32();
                if (index < 0)
                    throw new InvalidDataException($"Runtime entity world '{path}' has negative render object reference index {index} at slot {i}.");
                if (kind != RuntimeRenderObjectReferenceKind.Mesh
                    && kind != RuntimeRenderObjectReferenceKind.RefMaterial
                    && kind != RuntimeRenderObjectReferenceKind.CombinedMaterial
                    && kind != RuntimeRenderObjectReferenceKind.TerrainMaterial)
                {
                    throw new InvalidDataException($"Runtime entity world '{path}' has unknown render object reference kind {(int)kind} at slot {i}.");
                }
                references[i] = new RuntimeRenderObjectReference(kind, index);
            }

            entityPayloadLength = reader.ReadInt32();
            entityPayloadOffset = fs.Position;
            if (entityPayloadLength <= 0 || entityPayloadLength > fs.Length - entityPayloadOffset)
                throw new InvalidDataException($"Runtime entity world '{path}' has invalid entity payload length {entityPayloadLength}.");
        }

        public sealed class UnityObjectReferenceComparer : IEqualityComparer<Object>
        {
            public static readonly UnityObjectReferenceComparer Instance = new();

            public bool Equals(Object x, Object y)
                => ReferenceEquals(x, y);

            public int GetHashCode(Object obj)
                => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
