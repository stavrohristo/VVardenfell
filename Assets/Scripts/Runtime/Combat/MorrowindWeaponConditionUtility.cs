using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;

namespace VVardenfell.Runtime.Combat
{
    static class MorrowindWeaponConditionUtility
    {
        public static float ResolveEquippedConditionMultiplier(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity attacker,
            in ContentReference weaponContent,
            in ItemEquipmentDef weapon)
        {
            RequireContentDb(contentDb);
            if (weapon.Health <= 0)
                return 1f;

            var equipped = RequireEquippedWeaponSlot(entityManager, attacker, weaponContent);
            int condition = ResolveEquippedWeaponCondition(contentDb, entityManager, attacker, equipped, weapon);
            return condition / (float)weapon.Health;
        }

        public static void ApplyWeaponConditionDamage(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity attacker,
            in ContentReference weaponContent,
            in ItemEquipmentDef weapon,
            float adjustedWeaponDamage)
        {
            RequireContentDb(contentDb);
            if (weapon.Health <= 0)
                return;

            float weaponDamageMult = contentDb.RequireGameSettingFloat("fWeaponDamageMult");
            int conditionDamage = (int)math.max(1f, weaponDamageMult * math.max(0f, adjustedWeaponDamage));
            var equipped = RequireEquippedWeaponSlot(entityManager, attacker, weaponContent);

            if (entityManager.HasBuffer<PlayerInventoryItem>(attacker))
            {
                var inventory = entityManager.GetBuffer<PlayerInventoryItem>(attacker);
                if ((uint)equipped.InventoryIndex >= (uint)inventory.Length)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Player equipped weapon inventory index {equipped.InventoryIndex} is outside inventory length {inventory.Length}.");

                var item = inventory[equipped.InventoryIndex];
                ApplyConditionDamage(ref item, weaponContent, weapon, conditionDamage);
                inventory[equipped.InventoryIndex] = item;
                if (item.Condition <= 0)
                    RemoveEquippedWeaponSlot(entityManager, attacker, weaponContent);
                return;
            }

            if (entityManager.HasBuffer<ActorInventoryItem>(attacker))
            {
                var inventory = entityManager.GetBuffer<ActorInventoryItem>(attacker);
                if ((uint)equipped.InventoryIndex >= (uint)inventory.Length)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Actor equipped weapon inventory index {equipped.InventoryIndex} is outside inventory length {inventory.Length}.");

                var item = inventory[equipped.InventoryIndex];
                ApplyConditionDamage(ref item, weaponContent, weapon, conditionDamage);
                inventory[equipped.InventoryIndex] = item;
                if (item.Condition <= 0)
                    RemoveEquippedWeaponSlot(entityManager, attacker, weaponContent);
                return;
            }

            throw new InvalidOperationException($"[VVardenfell][Damage] Equipped weapon attacker entity={attacker.Index}:{attacker.Version} has no inventory buffer.");
        }

        static ActorEquipmentSlot RequireEquippedWeaponSlot(
            EntityManager entityManager,
            Entity attacker,
            in ContentReference weaponContent)
        {
            if (attacker == Entity.Null || !entityManager.Exists(attacker))
                throw new InvalidOperationException("[VVardenfell][Damage] Equipped weapon attacker entity is missing.");
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(attacker))
                throw new InvalidOperationException($"[VVardenfell][Damage] Equipped weapon attacker entity={attacker.Index}:{attacker.Version} has no ActorEquipmentSlot buffer.");

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(attacker, true);
            bool found = false;
            ActorEquipmentSlot result = default;
            for (int i = 0; i < equipment.Length; i++)
            {
                var candidate = equipment[i];
                if (candidate.Slot != ItemEquipmentSlot.Weapon)
                    continue;
                if (found)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Attacker entity={attacker.Index}:{attacker.Version} has multiple equipped weapon slots.");
                if (!Matches(candidate.Content, weaponContent))
                    throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit weapon {weaponContent.Kind}:{weaponContent.HandleValue} does not match equipped weapon content {candidate.Content.Kind}:{candidate.Content.HandleValue}.");

                result = candidate;
                found = true;
            }

            if (!found)
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit weapon {weaponContent.Kind}:{weaponContent.HandleValue} is not equipped in the weapon slot.");
            if (result.InventoryIndex < 0)
                throw new InvalidOperationException($"[VVardenfell][Damage] Equipped weapon has invalid inventory index {result.InventoryIndex}.");

            return result;
        }

        static int ResolveEquippedWeaponCondition(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity attacker,
            in ActorEquipmentSlot equipped,
            in ItemEquipmentDef weapon)
        {
            if (entityManager.HasBuffer<PlayerInventoryItem>(attacker))
            {
                var inventory = entityManager.GetBuffer<PlayerInventoryItem>(attacker, true);
                if ((uint)equipped.InventoryIndex >= (uint)inventory.Length)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Player equipped weapon inventory index {equipped.InventoryIndex} is outside inventory length {inventory.Length}.");

                return InventoryConditionUtility.RequireEquippedSingleConditionableStack(
                    inventory[equipped.InventoryIndex].Count,
                    inventory[equipped.InventoryIndex].Condition,
                    weapon,
                    equipped.Content);
            }

            if (entityManager.HasBuffer<ActorInventoryItem>(attacker))
            {
                var inventory = entityManager.GetBuffer<ActorInventoryItem>(attacker, true);
                if ((uint)equipped.InventoryIndex >= (uint)inventory.Length)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Actor equipped weapon inventory index {equipped.InventoryIndex} is outside inventory length {inventory.Length}.");

                return InventoryConditionUtility.RequireEquippedSingleConditionableStack(
                    inventory[equipped.InventoryIndex].Count,
                    inventory[equipped.InventoryIndex].Condition,
                    weapon,
                    equipped.Content);
            }

            throw new InvalidOperationException($"[VVardenfell][Damage] Equipped weapon attacker entity={attacker.Index}:{attacker.Version} has no inventory buffer.");
        }

        static void ApplyConditionDamage(
            ref PlayerInventoryItem item,
            in ContentReference weaponContent,
            in ItemEquipmentDef weapon,
            int conditionDamage)
        {
            int condition = InventoryConditionUtility.RequireEquippedSingleConditionableStack(item.Count, item.Condition, weapon, weaponContent);
            item.Condition = math.max(0, condition - conditionDamage);
        }

        static void ApplyConditionDamage(
            ref ActorInventoryItem item,
            in ContentReference weaponContent,
            in ItemEquipmentDef weapon,
            int conditionDamage)
        {
            int condition = InventoryConditionUtility.RequireEquippedSingleConditionableStack(item.Count, item.Condition, weapon, weaponContent);
            item.Condition = math.max(0, condition - conditionDamage);
        }

        static void RemoveEquippedWeaponSlot(
            EntityManager entityManager,
            Entity attacker,
            in ContentReference weaponContent)
        {
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(attacker))
                throw new InvalidOperationException($"[VVardenfell][Damage] Broken equipped weapon attacker entity={attacker.Index}:{attacker.Version} has no equipment buffer.");

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(attacker);
            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var candidate = equipment[i];
                if (candidate.Slot == ItemEquipmentSlot.Weapon && Matches(candidate.Content, weaponContent))
                {
                    equipment.RemoveAt(i);
                    return;
                }
            }

            throw new InvalidOperationException($"[VVardenfell][Damage] Broken equipped weapon {weaponContent.Kind}:{weaponContent.HandleValue} was not found on attacker entity={attacker.Index}:{attacker.Version}.");
        }

        static bool Matches(in ContentReference lhs, in ContentReference rhs)
            => lhs.Kind == rhs.Kind && lhs.HandleValue == rhs.HandleValue;

        static void RequireContentDb(RuntimeContentDatabase contentDb)
        {
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][Damage] Runtime content database is not loaded.");
        }
    }
}
