using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        const string GoldId = "gold_001";

        static DialogueServiceWindowViewModel BuildDialogueServiceModel(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            in MorrowindDialogueServiceWindowState service,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> stagedItems,
            DynamicBuffer<PlayerInventoryItem> playerInventory,
            in PlayerPresentationStats playerStats)
        {
            if (service.Visible == 0 || session.Active == 0)
                return null;

            return (MorrowindDialogueServiceKind)service.Mode switch
            {
                MorrowindDialogueServiceKind.Persuasion => BuildPersuasionModel(ref content, playerInventory),
                MorrowindDialogueServiceKind.Travel => BuildTravelModel(ref content, service, playerInventory, playerStats),
                MorrowindDialogueServiceKind.Barter => BuildBarterModel(ref content, entityManager, service, stagedItems, playerInventory),
                _ => null,
            };
        }

        static DialogueServiceWindowViewModel BuildPersuasionModel(
            ref RuntimeContentBlob content,
            DynamicBuffer<PlayerInventoryItem> playerInventory)
        {
            int gold = CountGold(ref content, playerInventory);
            return new DialogueServiceWindowViewModel
            {
                LayoutKind = DialogueServiceWindowLayoutKind.Persuasion,
                Title = ResolveGameSettingString(ref content, "sPersuasion", "Persuasion"),
                Header = ResolveGameSettingString(ref content, "sPersuasionMenuTitle", "What would you like to do?"),
                FooterText = $"{ResolveGameSettingString(ref content, "sGold", "Gold")}: {gold}",
                Buttons = new[]
                {
                    PersuasionButton(ref content, "sAdmire", "Admire", MorrowindPersuasionAction.Admire, true),
                    PersuasionButton(ref content, "sIntimidate", "Intimidate", MorrowindPersuasionAction.Intimidate, true),
                    PersuasionButton(ref content, "sTaunt", "Taunt", MorrowindPersuasionAction.Taunt, true),
                    PersuasionButton(ref content, "sBribe 10 Gold", "Bribe 10 Gold", MorrowindPersuasionAction.Bribe10, gold >= 10),
                    PersuasionButton(ref content, "sBribe 100 Gold", "Bribe 100 Gold", MorrowindPersuasionAction.Bribe100, gold >= 100),
                    PersuasionButton(ref content, "sBribe 1000 Gold", "Bribe 1000 Gold", MorrowindPersuasionAction.Bribe1000, gold >= 1000),
                    CloseButton(ref content),
                },
            };
        }

        static DialogueServiceButtonViewModel PersuasionButton(
            ref RuntimeContentBlob content,
            string gmst,
            string fallback,
            MorrowindPersuasionAction action,
            bool enabled)
            => new()
            {
                Text = ResolveGameSettingString(ref content, gmst, fallback),
                Enabled = enabled,
                Action = MorrowindDialogueServiceAction.Persuade,
                Int0 = (int)action,
            };

        static DialogueServiceWindowViewModel BuildTravelModel(
            ref RuntimeContentBlob content,
            in MorrowindDialogueServiceWindowState service,
            DynamicBuffer<PlayerInventoryItem> playerInventory,
            in PlayerPresentationStats playerStats)
        {
            if (!service.SpeakerActor.IsValid)
                return null;

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, service.SpeakerActor);
            RuntimeContentBlobUtility.RequireRange(actor.FirstTravelDestinationIndex, actor.TravelDestinationCount, content.ActorTravelDestinations.Length, "actor travel destination");
            int gold = CountGold(ref content, playerInventory);
            var rows = new List<DialogueServiceRowViewModel>(actor.TravelDestinationCount);
            for (int i = 0; i < actor.TravelDestinationCount; i++)
            {
                ref RuntimeActorTravelDestinationDefBlob destination = ref content.ActorTravelDestinations[actor.FirstTravelDestinationIndex + i];
                int price = EstimateTravelPrice(ref content, ref destination, playerStats);
                rows.Add(new DialogueServiceRowViewModel
                {
                    LeftText = ResolveTravelDestinationName(ref destination),
                    RightText = price.ToString(),
                    Enabled = gold >= price,
                    Action = MorrowindDialogueServiceAction.Travel,
                    Int0 = i,
                    Int1 = price,
                });
            }

            return new DialogueServiceWindowViewModel
            {
                Title = ResolveGameSettingString(ref content, "sTravel", "Travel"),
                Header = $"{ResolveGameSettingString(ref content, "sGold", "Gold")}: {gold}",
                Rows = rows.ToArray(),
                Buttons = new[] { CloseButton(ref content) },
            };
        }

        static DialogueServiceWindowViewModel BuildBarterModel(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            in MorrowindDialogueServiceWindowState service,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> stagedItems,
            DynamicBuffer<PlayerInventoryItem> playerInventory)
        {
            if (service.SpeakerEntity == Entity.Null
                || !entityManager.Exists(service.SpeakerEntity)
                || !entityManager.HasBuffer<ActorInventoryItem>(service.SpeakerEntity))
            {
                return null;
            }

            var merchantInventory = entityManager.GetBuffer<ActorInventoryItem>(service.SpeakerEntity, true);
            int playerGold = CountGold(ref content, playerInventory);
            int merchantGold = entityManager.HasComponent<ActorBarterState>(service.SpeakerEntity)
                ? entityManager.GetComponentData<ActorBarterState>(service.SpeakerEntity).Gold
                : service.SpeakerActor.IsValid ? RuntimeContentBlobUtility.Get(ref content, service.SpeakerActor).Gold : 0;
            int balance = EstimateBarterBalance(ref content, merchantInventory, playerInventory, stagedItems, service.BarterOffer);
            var rows = new List<DialogueServiceRowViewModel>(merchantInventory.Length + playerInventory.Length + 2);
            rows.Add(new DialogueServiceRowViewModel { LeftText = ResolveGameSettingString(ref content, "sSeller", "Merchant"), Enabled = false });
            for (int i = 0; i < merchantInventory.Length; i++)
            {
                var item = merchantInventory[i];
                if (item.Count <= 0 || IsGold(ref content, item.Content))
                    continue;
                rows.Add(BuildBarterRow(ref content, item.Content, item.Count, StagedCount(stagedItems, owner: 1, i), MorrowindDialogueServiceAction.StageMerchantItem, i));
            }

            rows.Add(new DialogueServiceRowViewModel { LeftText = ResolveGameSettingString(ref content, "sPlayer", "Player"), Enabled = false });
            for (int i = 0; i < playerInventory.Length; i++)
            {
                var item = playerInventory[i];
                if (item.Count <= 0 || IsGold(ref content, item.Content))
                    continue;
                rows.Add(BuildBarterRow(ref content, item.Content, item.Count, StagedCount(stagedItems, owner: 2, i), MorrowindDialogueServiceAction.StagePlayerItem, i));
            }

            return new DialogueServiceWindowViewModel
            {
                Title = ResolveGameSettingString(ref content, "sBarter", "Barter"),
                Header = $"{ResolveGameSettingString(ref content, "sGold", "Gold")}: {playerGold} / {merchantGold}   {ResolveGameSettingString(ref content, "sBalance", "Balance")}: {balance}",
                Rows = rows.ToArray(),
                Buttons = new[]
                {
                    new DialogueServiceButtonViewModel { Text = "-10", Action = MorrowindDialogueServiceAction.AdjustBarterOffer, Int0 = -10 },
                    new DialogueServiceButtonViewModel { Text = "+10", Action = MorrowindDialogueServiceAction.AdjustBarterOffer, Int0 = 10 },
                    new DialogueServiceButtonViewModel { Text = ResolveGameSettingString(ref content, "sOffer", "Offer"), Action = MorrowindDialogueServiceAction.OfferBarter },
                    new DialogueServiceButtonViewModel { Text = ResolveGameSettingString(ref content, "sReset", "Reset"), Action = MorrowindDialogueServiceAction.ResetBarter },
                    CloseButton(ref content),
                },
            };
        }

        static DialogueServiceRowViewModel BuildBarterRow(
            ref RuntimeContentBlob content,
            ContentReference item,
            int count,
            int staged,
            MorrowindDialogueServiceAction action,
            int index)
        {
            string name = RuntimeContentMetadataResolver.TryResolveCarryable(ref content, item, out var metadata)
                ? metadata.DisplayName
                : "Unknown item";
            return new DialogueServiceRowViewModel
            {
                LeftText = staged > 0 ? $"{name} ({staged}/{count})" : $"{name} ({count})",
                RightText = ResolveItemValue(ref content, item).ToString(),
                Enabled = staged < count,
                Action = action,
                Int0 = index,
            };
        }

        static DialogueServiceButtonViewModel CloseButton(ref RuntimeContentBlob content)
            => new()
            {
                Text = ResolveGameSettingString(ref content, "sCancel", "Cancel"),
                Action = MorrowindDialogueServiceAction.Close,
            };

        static int StagedCount(DynamicBuffer<MorrowindDialogueBarterStagedItem> staged, byte owner, int sourceIndex)
        {
            int count = 0;
            for (int i = 0; i < staged.Length; i++)
            {
                if (staged[i].Owner == owner && staged[i].SourceIndex == sourceIndex)
                    count += staged[i].Count;
            }
            return count;
        }

        static int EstimateBarterBalance(
            ref RuntimeContentBlob content,
            DynamicBuffer<ActorInventoryItem> merchantInventory,
            DynamicBuffer<PlayerInventoryItem> playerInventory,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> stagedItems,
            int offerAdjustment)
        {
            int balance = offerAdjustment;
            for (int i = 0; i < stagedItems.Length; i++)
            {
                var staged = stagedItems[i];
                int value = ResolveItemValue(ref content, staged.Content) * staged.Count;
                balance += staged.Owner == 1 ? -value : value;
            }
            return balance;
        }

        static int EstimateTravelPrice(
            ref RuntimeContentBlob content,
            ref RuntimeActorTravelDestinationDefBlob destination,
            in PlayerPresentationStats playerStats)
        {
            if (destination.CellNameHash != 0UL)
                return Math.Max(1, (int)MathF.Floor(RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentStableHash.HashId("fMagesGuildTravel"))));

            float distance = playerStats.HasPlayer
                ? math.distance(playerStats.Position, new float3(destination.PosX, destination.PosY, destination.PosZ))
                : 0f;
            float travelMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentStableHash.HashId("fTravelMult"));
            return Math.Max(1, (int)MathF.Floor(travelMult == 0f ? distance : distance / travelMult));
        }

        static string ResolveTravelDestinationName(ref RuntimeActorTravelDestinationDefBlob destination)
        {
            string cell = destination.CellName.ToString();
            if (!string.IsNullOrWhiteSpace(cell))
                return cell.Trim();

            return $"{destination.PosX:0}, {destination.PosZ:0}";
        }

        static int ResolveItemValue(ref RuntimeContentBlob content, ContentReference item)
        {
            if (!RuntimeContentMetadataResolver.TryResolveCarryable(ref content, item, out var metadata))
                return 0;
            return Math.Max(0, metadata.Value);
        }

        static int CountGold(ref RuntimeContentBlob content, DynamicBuffer<PlayerInventoryItem> inventory)
        {
            int count = 0;
            for (int i = 0; i < inventory.Length; i++)
            {
                if (IsGold(ref content, inventory[i].Content))
                    count += Math.Max(0, inventory[i].Count);
            }
            return count;
        }

        static bool IsGold(ref RuntimeContentBlob content, ContentReference item)
        {
            if (item.Kind != ContentReferenceKind.Item || item.HandleValue <= 0)
                return false;
            ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, new ItemDefHandle { Value = item.HandleValue });
            return def.IdHash == RuntimeContentStableHash.HashId(GoldId);
        }
    }
}
