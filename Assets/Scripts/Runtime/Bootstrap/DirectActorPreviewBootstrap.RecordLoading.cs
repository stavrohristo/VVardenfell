using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Bootstrap
{

    static partial class DirectActorPreviewBootstrap
    {
        static void ParseRaceRecord(EsmReader esm, Dictionary<string, RaceDef> target)
        {
            var race = new RaceDef();
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        race.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        race.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('R', 'A', 'D', 'T'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 140)
                            race.Flags = ReadInt32(bytes, 136);
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(race.Id))
                return;

            if (deleted)
            {
                target.Remove(race.Id);
                return;
            }

            race.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('R', 'A', 'C', 'E'), race.Id);
            target[race.Id] = race;
        }


        static void ParseNpcRecord(EsmReader esm, Dictionary<string, ActorDef> target)
        {
            var actor = new ActorDef
            {
                Kind = ActorDefKind.Npc,
                RecordTag = EsmFourCC.Make('N', 'P', 'C', '_'),
            };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        actor.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        actor.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        actor.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('R', 'N', 'A', 'M'):
                        actor.RaceId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('B', 'N', 'A', 'M'):
                        actor.HeadId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('K', 'N', 'A', 'M'):
                        actor.HairId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('F', 'L', 'A', 'G'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            actor.Flags = ReadUInt32(bytes, 0);
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
                return;

            if (deleted)
            {
                target.Remove(actor.Id);
                return;
            }

            actor.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('N', 'P', 'C', '_'), actor.Id);
            target[actor.Id] = actor;
        }


        static void ParseCreatureRecord(EsmReader esm, Dictionary<string, ActorDef> target)
        {
            var actor = new ActorDef
            {
                Kind = ActorDefKind.Creature,
                RecordTag = EsmFourCC.Make('C', 'R', 'E', 'A'),
                Scale = 1f,
            };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        actor.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        actor.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        actor.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('C', 'N', 'A', 'M'):
                        actor.OriginalId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('F', 'L', 'A', 'G'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            actor.Flags = ReadUInt32(bytes, 0);
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
                return;

            if (deleted)
            {
                target.Remove(actor.Id);
                return;
            }

            actor.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('C', 'R', 'E', 'A'), actor.Id);
            target[actor.Id] = actor;
        }


        static ushort ReadUInt16(byte[] bytes, int offset)
            => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));


        static short ReadInt16(byte[] bytes, int offset)
            => (short)ReadUInt16(bytes, offset);


        static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));


        static int ReadInt32(byte[] bytes, int offset)
            => unchecked((int)ReadUInt32(bytes, offset));


        }
    }
