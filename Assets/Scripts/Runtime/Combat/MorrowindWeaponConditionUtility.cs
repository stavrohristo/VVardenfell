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
            int condition = ActorEquipmentConditionUtility.RequireEquippedCondition(equipped, weapon);
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

            var equipment = RequireEquipmentBuffer(entityManager, attacker);
            int slotIndex = RequireEquippedWeaponSlotIndex(equipment, attacker, weaponContent);
            var equipped = equipment[slotIndex];
            ActorEquipmentConditionUtility.ApplyConditionDamage(ref equipped, weapon, conditionDamage);
            if (equipped.Condition <= 0)
                equipment.RemoveAt(slotIndex);
            else
                equipment[slotIndex] = equipped;
        }

        static ActorEquipmentSlot RequireEquippedWeaponSlot(
            EntityManager entityManager,
            Entity attacker,
            in ContentReference weaponContent)
        {
            var equipment = RequireEquipmentBuffer(entityManager, attacker, true);
            return equipment[RequireEquippedWeaponSlotIndex(equipment, attacker, weaponContent)];
        }

        static DynamicBuffer<ActorEquipmentSlot> RequireEquipmentBuffer(
            EntityManager entityManager,
            Entity attacker,
            bool readOnly = false)
        {
            if (attacker == Entity.Null || !entityManager.Exists(attacker))
                throw new InvalidOperationException("[VVardenfell][Damage] Equipped weapon attacker entity is missing.");
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(attacker))
                throw new InvalidOperationException($"[VVardenfell][Damage] Equipped weapon attacker entity={attacker.Index}:{attacker.Version} has no ActorEquipmentSlot buffer.");

            return entityManager.GetBuffer<ActorEquipmentSlot>(attacker, readOnly);
        }

        static int RequireEquippedWeaponSlotIndex(
            DynamicBuffer<ActorEquipmentSlot> equipment,
            Entity attacker,
            in ContentReference weaponContent)
        {
            int result = -1;
            for (int i = 0; i < equipment.Length; i++)
            {
                var candidate = equipment[i];
                if (candidate.Slot != ItemEquipmentSlot.Weapon)
                    continue;
                if (result >= 0)
                    throw new InvalidOperationException($"[VVardenfell][Damage] Attacker entity={attacker.Index}:{attacker.Version} has multiple equipped weapon slots.");
                if (!Matches(candidate.Content, weaponContent))
                    throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit weapon {weaponContent.Kind}:{weaponContent.HandleValue} does not match equipped weapon content {candidate.Content.Kind}:{candidate.Content.HandleValue}.");

                result = i;
            }

            if (result < 0)
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit weapon {weaponContent.Kind}:{weaponContent.HandleValue} is not equipped in the weapon slot.");
            if (equipment[result].InventoryIndex < 0)
                throw new InvalidOperationException($"[VVardenfell][Damage] Equipped weapon has invalid inventory index {equipment[result].InventoryIndex}.");

            return result;
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
