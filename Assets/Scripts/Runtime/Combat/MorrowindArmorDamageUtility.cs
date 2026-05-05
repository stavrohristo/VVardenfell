using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Combat
{
    static class MorrowindArmorDamageUtility
    {
        static readonly short ShieldEffectId = RequireEffectId("sEffectShield");

        public static float ApplyArmorToHealthDamage(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity target,
            float damage,
            uint roll0To99,
            out MorrowindArmorImpact impact)
        {
            impact = default;
            RequireContentDb(contentDb);
            if (damage <= 0f)
                return damage;

            float armor = ComputeArmorRating(contentDb, entityManager, target);
            float divisor = damage + armor;
            if (divisor <= 0f)
                throw new InvalidOperationException($"[VVardenfell][Damage] Invalid armor divisor for target entity={target.Index}:{target.Version}: damage={damage}, armor={armor}.");

            float armorMult = damage / divisor;
            float armorAdjusted = damage * math.max(armorMult, contentDb.RequireGameSettingFloat("fCombatArmorMinMult"));
            float finalDamage = math.max(armorAdjusted, 1f);
            impact = ResolveArmorImpact(contentDb, entityManager, target, roll0To99, damage, armorAdjusted);
            return finalDamage;
        }

        public static void ApplyArmorConditionDamage(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            in MorrowindArmorImpact impact)
        {
            RequireContentDb(contentDb);
            if (impact.HasEquippedArmor == 0 || impact.ConditionDamage <= 0)
                return;
            if (impact.Target == Entity.Null || !entityManager.Exists(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Armor condition target entity is missing.");

            if (!TryGetEquippedSlot(entityManager, impact.Target, impact.Slot, impact.Content, out var equipped))
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor impact slot {impact.Slot} is no longer equipped on target entity={impact.Target.Index}:{impact.Target.Version}.");
            if (equipped.InventoryIndex < 0)
                throw new InvalidOperationException($"[VVardenfell][Damage] Equipped armor slot {impact.Slot} has invalid inventory index {equipped.InventoryIndex}.");

            ApplyEquippedSlotConditionDamage(contentDb, entityManager, impact.Target, impact.Slot, impact.Content, impact.ConditionDamage);
        }

        public static void ApplyShieldBlockConditionDamage(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity target,
            in ContentReference shieldContent,
            int conditionDamage)
        {
            RequireContentDb(contentDb);
            if (conditionDamage <= 0)
                return;
            if (target == Entity.Null || !entityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][Damage] Shield block condition target entity is missing.");

            if (!TryGetEquippedSlot(entityManager, target, ItemEquipmentSlot.Shield, shieldContent, out var equipped))
                throw new InvalidOperationException($"[VVardenfell][Damage] Block shield is no longer equipped on target entity={target.Index}:{target.Version}.");
            if (equipped.InventoryIndex < 0)
                throw new InvalidOperationException($"[VVardenfell][Damage] Block shield has invalid inventory index {equipped.InventoryIndex}.");

            ApplyEquippedSlotConditionDamage(contentDb, entityManager, target, ItemEquipmentSlot.Shield, shieldContent, conditionDamage);
        }

        static float ComputeArmorRating(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity actor)
        {
            if (actor == Entity.Null || !entityManager.Exists(actor))
                throw new InvalidOperationException("[VVardenfell][Damage] Armor rating target entity is missing.");
            if (!entityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor rating target entity={actor.Index}:{actor.Version} has no ActorActiveMagicEffect buffer.");

            var effects = entityManager.GetBuffer<ActorActiveMagicEffect>(actor, true);
            float magicShield = MorrowindMeleeCombatMechanics.SumEffectMagnitude(effects, ShieldEffectId);
            if (IsCreature(contentDb, entityManager, actor))
                return magicShield;

            if (!entityManager.HasComponent<ActorSkillSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor rating target entity={actor.Index}:{actor.Version} has no ActorSkillSet.");
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(actor))
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor rating target entity={actor.Index}:{actor.Version} has no ActorEquipmentSlot buffer.");

            var skills = entityManager.GetComponentData<ActorSkillSet>(actor);
            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(actor, true);
            float unarmored = ComputeUnarmoredRating(contentDb, skills);

            return ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.Cuirass, unarmored) * 0.3f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.Shield, unarmored) * 0.1f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.Helmet, unarmored) * 0.1f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.Greaves, unarmored) * 0.1f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.Boots, unarmored) * 0.1f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.LeftPauldron, unarmored) * 0.1f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.RightPauldron, unarmored) * 0.1f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.LeftHand, unarmored) * 0.05f
                   + ComputeSlotRating(contentDb, entityManager, actor, equipment, skills, ItemEquipmentSlot.RightHand, unarmored) * 0.05f
                   + magicShield;
        }

        static bool IsCreature(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity actor)
        {
            if (entityManager.HasComponent<PlayerTag>(actor))
                return false;
            if (!entityManager.HasComponent<ActorSpawnSource>(actor))
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor rating target entity={actor.Index}:{actor.Version} has no ActorSpawnSource.");

            var source = entityManager.GetComponentData<ActorSpawnSource>(actor);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor rating target entity={actor.Index}:{actor.Version} has invalid actor definition.");

            ref readonly var actorDef = ref contentDb.Get(source.Definition);
            return actorDef.Kind == ActorDefKind.Creature;
        }

        static float ComputeSlotRating(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity actor,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            in ActorSkillSet skills,
            ItemEquipmentSlot slot,
            float unarmoredRating)
        {
            if (!TryGetEquippedArmor(contentDb, equipment, slot, out var equipped, out var armor))
                return unarmoredRating;

            float skill = ResolveArmorSkill(contentDb, armor, skills);
            float conditionMult = ResolveEquippedConditionMultiplier(contentDb, entityManager, actor, equipped, armor);
            if (armor.Weight == 0f)
                return armor.Armor * conditionMult;

            return armor.Armor * skill / contentDb.RequireGameSettingInt("iBaseArmorSkill") * conditionMult;
        }

        public static bool TryGetEquippedArmor(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ItemEquipmentSlot slot,
            out ActorEquipmentSlot equippedArmor,
            out ItemEquipmentDef armor)
        {
            equippedArmor = default;
            armor = default;
            bool found = false;
            for (int i = 0; i < equipment.Length; i++)
            {
                var equipped = equipment[i];
                if (equipped.Slot != slot)
                    continue;
                if (found)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Actor has multiple equipped items in armor slot {slot}.");
                if (!equipped.Content.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Actor has invalid equipped content in armor slot {slot}.");
                if (equipped.Content.Kind != ContentReferenceKind.Item)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Actor has non-item equipped content kind {equipped.Content.Kind} in armor slot {slot}.");

                var handle = new ItemDefHandle { Value = equipped.Content.HandleValue };
                if (!contentDb.TryGetItemEquipment(handle, out var equipmentDef))
                    throw new InvalidOperationException($"[VVardenfell][Damage] Equipped item handle {handle.Value} in armor slot {slot} has no equipment definition.");

                found = true;
                if (equipmentDef.Kind == ItemEquipmentKind.Armor)
                {
                    equippedArmor = equipped;
                    armor = equipmentDef;
                }
            }

            return armor.Kind == ItemEquipmentKind.Armor;
        }

        static MorrowindArmorImpact ResolveArmorImpact(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity target,
            uint roll0To99,
            float originalDamage,
            float adjustedDamage)
        {
            var slot = PickRandomArmorSlot(roll0To99);
            var impact = new MorrowindArmorImpact
            {
                Target = target,
                Slot = slot,
                Skill = ActorSkillKind.Unarmored,
                ConditionDamage = math.max(0, (int)math.floor(originalDamage - adjustedDamage)),
            };

            if (!entityManager.HasBuffer<ActorEquipmentSlot>(target))
                return impact;

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(target, true);
            if (!TryGetEquippedArmor(contentDb, equipment, slot, out _, out var armor))
                return impact;

            impact.HasEquippedArmor = 1;
            impact.Content = FindEquippedArmorContent(contentDb, equipment, slot);
            impact.Skill = ResolveArmorSkillKind(contentDb, armor);
            return impact;
        }

        static ContentReference FindEquippedArmorContent(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ItemEquipmentSlot slot)
        {
            if (!TryGetEquippedArmor(contentDb, equipment, slot, out var equipped, out _))
                return default;

            return equipped.Content;
        }

        static ItemEquipmentSlot PickRandomArmorSlot(uint roll0To99)
        {
            uint roll = roll0To99 % 100u;
            if (roll >= 90u)
                return ItemEquipmentSlot.Shield;
            if (roll >= 85u)
                return ItemEquipmentSlot.RightHand;
            if (roll >= 80u)
                return ItemEquipmentSlot.LeftHand;
            if (roll >= 70u)
                return ItemEquipmentSlot.RightPauldron;
            if (roll >= 60u)
                return ItemEquipmentSlot.LeftPauldron;
            if (roll >= 50u)
                return ItemEquipmentSlot.Boots;
            if (roll >= 40u)
                return ItemEquipmentSlot.Greaves;
            if (roll >= 30u)
                return ItemEquipmentSlot.Helmet;

            return ItemEquipmentSlot.Cuirass;
        }

        static float ResolveEquippedConditionMultiplier(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity actor,
            in ActorEquipmentSlot equipped,
            in ItemEquipmentDef armor)
        {
            if (armor.Health <= 0)
                return 1f;

            int condition = ActorEquipmentConditionUtility.RequireEquippedCondition(equipped, armor);
            return condition / (float)armor.Health;
        }

        static bool TryGetEquippedSlot(
            EntityManager entityManager,
            Entity actor,
            ItemEquipmentSlot slot,
            in ContentReference content,
            out ActorEquipmentSlot equipped)
        {
            equipped = default;
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(actor))
                return false;

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(actor, true);
            for (int i = 0; i < equipment.Length; i++)
            {
                var candidate = equipment[i];
                if (candidate.Slot == slot
                    && candidate.Content.Kind == content.Kind
                    && candidate.Content.HandleValue == content.HandleValue)
                {
                    equipped = candidate;
                    return true;
                }
            }

            return false;
        }

        static void ApplyEquippedSlotConditionDamage(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity actor,
            ItemEquipmentSlot slot,
            in ContentReference content,
            int conditionDamage)
        {
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(actor))
                throw new InvalidOperationException($"[VVardenfell][Damage] Equipped armor target entity={actor.Index}:{actor.Version} has no equipment buffer.");

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(actor);
            int slotIndex = FindEquippedSlotIndex(equipment, slot, content);
            if (slotIndex < 0)
                throw new InvalidOperationException($"[VVardenfell][Damage] Equipped armor slot {slot} was not found on target entity={actor.Index}:{actor.Version}.");

            var equipped = equipment[slotIndex];
            var equipmentDef = RequireArmorEquipment(contentDb, content);
            ActorEquipmentConditionUtility.ApplyConditionDamage(ref equipped, equipmentDef, conditionDamage);
            if (equipmentDef.Health > 0 && equipped.Condition <= 0)
                equipment.RemoveAt(slotIndex);
            else
                equipment[slotIndex] = equipped;
        }

        static void RemoveEquippedSlot(
            EntityManager entityManager,
            Entity actor,
            ItemEquipmentSlot slot,
            in ContentReference content)
        {
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(actor))
                throw new InvalidOperationException($"[VVardenfell][Damage] Broken equipped armor target entity={actor.Index}:{actor.Version} has no equipment buffer.");

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(actor);
            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var candidate = equipment[i];
                if (candidate.Slot == slot
                    && candidate.Content.Kind == content.Kind
                    && candidate.Content.HandleValue == content.HandleValue)
                {
                    equipment.RemoveAt(i);
                    return;
                }
            }

            throw new InvalidOperationException($"[VVardenfell][Damage] Broken equipped armor slot {slot} was not found on target entity={actor.Index}:{actor.Version}.");
        }

        static int FindEquippedSlotIndex(
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ItemEquipmentSlot slot,
            in ContentReference content)
        {
            for (int i = 0; i < equipment.Length; i++)
            {
                var candidate = equipment[i];
                if (candidate.Slot == slot
                    && candidate.Content.Kind == content.Kind
                    && candidate.Content.HandleValue == content.HandleValue)
                {
                    return i;
                }
            }

            return -1;
        }

        static ItemEquipmentDef RequireArmorEquipment(RuntimeContentDatabase contentDb, in ContentReference content)
        {
            if (!content.IsValid || content.Kind != ContentReferenceKind.Item)
                throw new InvalidOperationException("[VVardenfell][Damage] Armor impact is missing item content.");

            var handle = new ItemDefHandle { Value = content.HandleValue };
            if (!contentDb.TryGetItemEquipment(handle, out var equipment) || equipment.Kind != ItemEquipmentKind.Armor)
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor impact item handle {handle.Value} does not resolve to armor equipment.");

            return equipment;
        }

        static float ComputeUnarmoredRating(RuntimeContentDatabase contentDb, in ActorSkillSet skills)
        {
            float unarmoredSkill = skills.Unarmored;
            return (contentDb.RequireGameSettingFloat("fUnarmoredBase1") * unarmoredSkill)
                   * (contentDb.RequireGameSettingFloat("fUnarmoredBase2") * unarmoredSkill);
        }

        static float ResolveArmorSkill(RuntimeContentDatabase contentDb, in ItemEquipmentDef armor, in ActorSkillSet skills)
        {
            return ResolveArmorSkillKind(contentDb, armor) switch
            {
                ActorSkillKind.LightArmor => skills.LightArmor,
                ActorSkillKind.MediumArmor => skills.MediumArmor,
                ActorSkillKind.HeavyArmor => skills.HeavyArmor,
                _ => throw new InvalidOperationException("[VVardenfell][Damage] Armor resolved to a non-armor skill."),
            };
        }

        public static ActorSkillKind ResolveArmorSkillKind(RuntimeContentDatabase contentDb, in ItemEquipmentDef armor)
        {
            float baseWeight = contentDb.RequireGameSettingFloat(ResolveArmorTypeWeightGmst(armor.Type));
            const float epsilon = 0.0005f;
            if (armor.Weight <= baseWeight * contentDb.RequireGameSettingFloat("fLightMaxMod") + epsilon)
                return ActorSkillKind.LightArmor;
            if (armor.Weight <= baseWeight * contentDb.RequireGameSettingFloat("fMedMaxMod") + epsilon)
                return ActorSkillKind.MediumArmor;

            return ActorSkillKind.HeavyArmor;
        }

        static string ResolveArmorTypeWeightGmst(int armorType)
        {
            return armorType switch
            {
                0 => "iHelmWeight",
                1 => "iCuirassWeight",
                2 => "iPauldronWeight",
                3 => "iPauldronWeight",
                4 => "iGreavesWeight",
                5 => "iBootsWeight",
                6 => "iGauntletWeight",
                7 => "iGauntletWeight",
                8 => "iShieldWeight",
                9 => "iGauntletWeight",
                10 => "iGauntletWeight",
                _ => throw new InvalidOperationException($"[VVardenfell][Damage] Unsupported armor type {armorType}."),
            };
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Damage] Unknown magic effect GMST id '{gmstId}'.");

            return effectId;
        }

        static void RequireContentDb(RuntimeContentDatabase contentDb)
        {
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][Damage] Runtime content database is not loaded.");
        }
    }
}
