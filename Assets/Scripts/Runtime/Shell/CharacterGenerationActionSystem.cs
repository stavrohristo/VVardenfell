using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    public partial struct CharacterGenerationActionSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorIdentitySet>(),
                ComponentType.ReadWrite<ActorAttributeSet>(),
                ComponentType.ReadWrite<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorVitalSet>());

            systemState.RequireForUpdate<CharacterGenerationState>();
            systemState.RequireForUpdate<CharacterGenerationRequest>();
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob content = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            ref var charGen = ref SystemAPI.GetSingletonRW<CharacterGenerationState>().ValueRW;
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            Entity charGenEntity = SystemAPI.GetSingletonEntity<CharacterGenerationState>();

            if (charGen.Initialized == 0)
                InitializeFromPlayer(ref systemState, ref content, ref charGen);

            var requestEntity = SystemAPI.GetSingletonEntity<CharacterGenerationRequest>();
            var request = SystemAPI.GetComponent<CharacterGenerationRequest>(requestEntity);
            if (request.Pending == 0)
            {
                shell.CharacterGenerationOpen = (byte)(((CharacterGenerationMenu)charGen.CurrentMenu) == CharacterGenerationMenu.None ? 0 : 1);
                return;
            }

            HandleRequest(ref systemState, charGenEntity, ref content, ref charGen, ref shell, request);
            request = default;
            SystemAPI.SetComponent(requestEntity, request);
        }

        void InitializeFromPlayer(ref SystemState systemState, ref RuntimeContentBlob content, ref CharacterGenerationState charGen)
        {
            byte currentMenu = charGen.CurrentMenu;
            ActorIdentitySet identity = ActorIdentitySet.DefaultPlayer();
            if (!_playerQuery.IsEmptyIgnoreFilter)
                identity = _playerQuery.GetSingleton<ActorIdentitySet>();
            charGen = CharacterGenerationUtility.CreateInitialState(ref content, identity);
            charGen.CurrentMenu = currentMenu;
        }

        void HandleRequest(
            ref SystemState systemState,
            Entity charGenEntity,
            ref RuntimeContentBlob content,
            ref CharacterGenerationState charGen,
            ref RuntimeShellState shell,
            in CharacterGenerationRequest request)
        {
            var action = (CharacterGenerationAction)request.Action;
            switch (action)
            {
                case CharacterGenerationAction.OpenMenu:
                    OpenRequestedMenu(systemState.EntityManager, charGenEntity, ref charGen, ref shell, (CharacterGenerationMenu)request.Menu);
                    break;
                case CharacterGenerationAction.Back:
                    Back(ref systemState, charGenEntity, ref content, ref charGen, ref shell);
                    break;
                case CharacterGenerationAction.ChooseName:
                    charGen.CharacterName = ToFixed64Checked(request.Text.ToString().Trim(), "name");
                    Apply(ref systemState, ref content, ref charGen);
                    HandleDialogDone(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationStage.NameChosen, CharacterGenerationMenu.Race);
                    break;
                case CharacterGenerationAction.ChooseRace:
                    RequireId(request.Id, "race");
                    CharacterGenerationUtility.RequireRace(ref content, request.Id);
                    bool raceChanged = !string.Equals(charGen.RaceId.ToString(), request.Id.ToString(), StringComparison.OrdinalIgnoreCase);
                    charGen.RaceId = request.Id;
                    if (raceChanged)
                        ResetSelectedBodyParts(ref content, ref charGen);
                    else
                        EnsureSelectedBodyParts(ref content, ref charGen);
                    Apply(ref systemState, ref content, ref charGen);
                    if (request.Byte0 != 0)
                    {
                        HandleDialogDone(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationStage.RaceChosen, CharacterGenerationMenu.ClassChoice);
                    }
                    break;
                case CharacterGenerationAction.AdjustGender:
                    charGen.Male = request.Byte0 == 0 ? (byte)0 : (byte)1;
                    ResetSelectedBodyParts(ref content, ref charGen);
                    Apply(ref systemState, ref content, ref charGen);
                    break;
                case CharacterGenerationAction.AdjustHead:
                    RequireId(request.Id, "head");
                    RequirePlayableBodyPart(ref content, charGen.RaceId, charGen.Male != 0, ActorBodyPartMeshPart.Head, request.Id);
                    charGen.HeadId = request.Id;
                    Apply(ref systemState, ref content, ref charGen);
                    break;
                case CharacterGenerationAction.AdjustHair:
                    RequireId(request.Id, "hair");
                    RequirePlayableBodyPart(ref content, charGen.RaceId, charGen.Male != 0, ActorBodyPartMeshPart.Hair, request.Id);
                    charGen.HairId = request.Id;
                    Apply(ref systemState, ref content, ref charGen);
                    break;
                case CharacterGenerationAction.ChooseClassChoice:
                    ChooseClassPath(systemState.EntityManager, charGenEntity, ref content, ref charGen, ref shell, (CharacterGenerationClassChoice)request.Byte0);
                    break;
                case CharacterGenerationAction.PickClass:
                    RequireId(request.Id, "class");
                    CharacterGenerationUtility.RequireClass(ref content, request.Id);
                    charGen.ClassId = request.Id;
                    charGen.CustomClassActive = 0;
                    Apply(ref systemState, ref content, ref charGen);
                    if (request.Byte0 != 0)
                        HandleDialogDone(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationStage.ClassChosen, CharacterGenerationMenu.Birth);
                    break;
                case CharacterGenerationAction.SetCustomClassName:
                {
                    FixedString512Bytes text = request.Text;
                    MutateCustomClass(ref systemState, (ref PlayerCustomClass custom) =>
                    {
                        RequireText(text, "custom class name");
                        custom.Active = 1;
                        custom.Id = ToFixed64Checked(text.ToString().Trim(), "custom class name");
                        custom.Name = custom.Id;
                    });
                    break;
                }
                case CharacterGenerationAction.SetCustomClassDescription:
                {
                    FixedString512Bytes text = request.Text;
                    MutateCustomClass(ref systemState, (ref PlayerCustomClass custom) => custom.Description = text);
                    break;
                }
                case CharacterGenerationAction.SetCustomClassSpecialization:
                {
                    int specialization = request.Int0;
                    MutateCustomClass(ref systemState, (ref PlayerCustomClass custom) => custom.Specialization = RequireRange(specialization, 0, 2, "custom class specialization"));
                    break;
                }
                case CharacterGenerationAction.SetCustomClassAttribute:
                {
                    int slot = request.Int0;
                    int attribute = request.Int1;
                    MutateCustomClass(ref systemState, (ref PlayerCustomClass custom) => SetCustomAttribute(ref custom, slot, RequireRange(attribute, 0, 7, "custom class attribute")));
                    break;
                }
                case CharacterGenerationAction.SetCustomClassSkill:
                {
                    int slot = request.Int0;
                    int skill = request.Int1;
                    MutateCustomClass(ref systemState, (ref PlayerCustomClass custom) => SetCustomSkill(ref custom, slot, RequireRange(skill, 0, 26, "custom class skill")));
                    break;
                }
                case CharacterGenerationAction.AcceptCustomClass:
                    if (request.Byte0 != 0 || !request.Text.IsEmpty)
                    {
                        FixedString512Bytes text = request.Text;
                        MutateCustomClass(ref systemState, (ref PlayerCustomClass custom) =>
                        {
                            RequireText(text, "custom class name");
                            custom.Active = 1;
                            custom.Id = ToFixed64Checked(text.ToString().Trim(), "custom class name");
                            custom.Name = custom.Id;
                        });
                    }
                    ValidateCustomClass(ref systemState);
                    charGen.CustomClassActive = 1;
                    Apply(ref systemState, ref content, ref charGen);
                    HandleDialogDone(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationStage.ClassChosen, CharacterGenerationMenu.Birth);
                    break;
                case CharacterGenerationAction.AnswerGenerateQuestion:
                    AnswerGenerateQuestion(ref content, ref charGen, request.Int0);
                    OpenRequestedMenu(
                        systemState.EntityManager,
                        charGenEntity,
                        ref charGen,
                        ref shell,
                        charGen.GenerateStep >= 10 ? CharacterGenerationMenu.ClassGenerateResult : CharacterGenerationMenu.ClassGenerateQuestion);
                    break;
                case CharacterGenerationAction.AcceptGeneratedClass:
                    if (charGen.GeneratedClassId.IsEmpty)
                        throw new InvalidOperationException("[VVardenfell][CharGen] Generated class cannot be accepted before all questions are answered.");
                    CharacterGenerationUtility.RequireClass(ref content, charGen.GeneratedClassId);
                    charGen.ClassId = charGen.GeneratedClassId;
                    charGen.CustomClassActive = 0;
                    Apply(ref systemState, ref content, ref charGen);
                    HandleDialogDone(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationStage.ClassChosen, CharacterGenerationMenu.Birth);
                    break;
                case CharacterGenerationAction.ChooseBirthsign:
                    RequireId(request.Id, "birthsign");
                    CharacterGenerationUtility.RequireBirthsign(ref content, request.Id);
                    charGen.BirthsignId = request.Id;
                    if (request.Byte0 != 0)
                        HandleDialogDone(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationStage.BirthSignChosen, CharacterGenerationMenu.Review);
                    Apply(ref systemState, ref content, ref charGen);
                    break;
                case CharacterGenerationAction.ReviewOpenName:
                    charGen.Stage = (byte)CharacterGenerationStage.ReviewNext;
                    OpenRequestedMenu(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.Name);
                    break;
                case CharacterGenerationAction.ReviewOpenRace:
                    charGen.Stage = (byte)CharacterGenerationStage.ReviewNext;
                    OpenRequestedMenu(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.Race);
                    break;
                case CharacterGenerationAction.ReviewOpenClass:
                    charGen.Stage = (byte)CharacterGenerationStage.ReviewNext;
                    OpenRequestedMenu(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.ClassChoice);
                    break;
                case CharacterGenerationAction.ReviewOpenBirthsign:
                    charGen.Stage = (byte)CharacterGenerationStage.ReviewNext;
                    OpenRequestedMenu(systemState.EntityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.Birth);
                    break;
                case CharacterGenerationAction.AcceptReview:
                    Apply(ref systemState, ref content, ref charGen);
                    charGen.Finalized = 1;
                    charGen.Stage = (byte)CharacterGenerationStage.ReviewNext;
                    CharacterGenerationUtility.Close(ref charGen);
                    RuntimeShellStateUtility.CloseCharacterGeneration(ref shell);
                    RemoveCharGenStage(systemState.EntityManager, charGenEntity);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][CharGen] Unsupported character generation action {request.Action}.");
            }

            shell.CharacterGenerationOpen = (byte)(((CharacterGenerationMenu)charGen.CurrentMenu) == CharacterGenerationMenu.None ? 0 : 1);
        }

        void OpenRequestedMenu(
            EntityManager entityManager,
            Entity charGenEntity,
            ref CharacterGenerationState charGen,
            ref RuntimeShellState shell,
            CharacterGenerationMenu menu)
        {
            if (menu == CharacterGenerationMenu.None)
            {
                CharacterGenerationUtility.Close(ref charGen);
                RuntimeShellStateUtility.CloseCharacterGeneration(ref shell);
                EnsureCharGenStage(entityManager, charGenEntity, in charGen);
                return;
            }

            ApplyOpenMenuStageParity(ref charGen, menu);
            CharacterGenerationUtility.OpenMenu(ref charGen, menu);
            EnsureCharGenStage(entityManager, charGenEntity, in charGen);
            RuntimeShellStateUtility.OpenCharacterGeneration(ref shell);
        }

        void Back(
            ref SystemState systemState,
            Entity charGenEntity,
            ref RuntimeContentBlob content,
            ref CharacterGenerationState charGen,
            ref RuntimeShellState shell)
        {
            if ((CharacterGenerationMenu)charGen.CurrentMenu == CharacterGenerationMenu.Race)
                Apply(ref systemState, ref content, ref charGen);
            else if ((CharacterGenerationMenu)charGen.CurrentMenu == CharacterGenerationMenu.ClassGenerateResult)
            {
                if (charGen.GeneratedClassId.IsEmpty)
                    throw new InvalidOperationException("[VVardenfell][CharGen] Generated class result cannot be applied before all questions are answered.");
                CharacterGenerationUtility.RequireClass(ref content, charGen.GeneratedClassId);
                charGen.ClassId = charGen.GeneratedClassId;
                charGen.CustomClassActive = 0;
                Apply(ref systemState, ref content, ref charGen);
            }
            else if ((CharacterGenerationMenu)charGen.CurrentMenu == CharacterGenerationMenu.ClassPick
                || (CharacterGenerationMenu)charGen.CurrentMenu == CharacterGenerationMenu.ClassCreate
                || (CharacterGenerationMenu)charGen.CurrentMenu == CharacterGenerationMenu.Birth)
            {
                if ((CharacterGenerationMenu)charGen.CurrentMenu != CharacterGenerationMenu.ClassCreate || IsCustomClassComplete(ref systemState))
                    Apply(ref systemState, ref content, ref charGen);
            }

            CharacterGenerationMenu next = (CharacterGenerationMenu)charGen.CurrentMenu switch
            {
                CharacterGenerationMenu.Race => CharacterGenerationMenu.Name,
                CharacterGenerationMenu.ClassChoice => CharacterGenerationMenu.Race,
                CharacterGenerationMenu.ClassPick => CharacterGenerationMenu.ClassChoice,
                CharacterGenerationMenu.ClassCreate => CharacterGenerationMenu.ClassChoice,
                CharacterGenerationMenu.ClassGenerateQuestion => CharacterGenerationMenu.ClassChoice,
                CharacterGenerationMenu.ClassGenerateResult => CharacterGenerationMenu.ClassChoice,
                CharacterGenerationMenu.Birth => CharacterGenerationMenu.ClassChoice,
                CharacterGenerationMenu.Review => CharacterGenerationMenu.Birth,
                _ => CharacterGenerationMenu.None,
            };
            OpenRequestedMenu(systemState.EntityManager, charGenEntity, ref charGen, ref shell, next);
            if (next == CharacterGenerationMenu.Birth)
            {
                charGen.Stage = (byte)CharacterGenerationStage.ReviewBack;
                EnsureCharGenStage(systemState.EntityManager, charGenEntity, in charGen);
            }
        }

        void ChooseClassPath(EntityManager entityManager, Entity charGenEntity, ref RuntimeContentBlob content, ref CharacterGenerationState charGen, ref RuntimeShellState shell, CharacterGenerationClassChoice choice)
        {
            switch (choice)
            {
                case CharacterGenerationClassChoice.Generate:
                    charGen.GenerateStep = 0;
                    charGen.GenerateCombat = 0;
                    charGen.GenerateMagic = 0;
                    charGen.GenerateStealth = 0;
                    charGen.GeneratedClassId = default;
                    OpenRequestedMenu(entityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.ClassGenerateQuestion);
                    break;
                case CharacterGenerationClassChoice.Pick:
                    OpenRequestedMenu(entityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.ClassPick);
                    break;
                case CharacterGenerationClassChoice.Create:
                    EnsureDefaultCustomClass(entityManager, ref content);
                    OpenRequestedMenu(entityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.ClassCreate);
                    break;
                case CharacterGenerationClassChoice.Back:
                    OpenRequestedMenu(entityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.Race);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][CharGen] Unsupported class choice {choice}.");
            }
        }

        void EnsureDefaultCustomClass(EntityManager entityManager, ref RuntimeContentBlob content)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][CharGen] Cannot initialize custom class without a live player.");

            Entity player = _playerQuery.GetSingletonEntity();
            if (!entityManager.HasComponent<PlayerCustomClass>(player))
                throw new InvalidOperationException("[VVardenfell][CharGen] Player has no custom class component.");

            var custom = entityManager.GetComponentData<PlayerCustomClass>(player);
            if (custom.Active != 0 && !custom.Name.IsEmpty)
                return;

            string name = RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentStableHash.HashId("sCustomClassName")).Trim();
            custom.Active = 1;
            custom.Id = ToFixed64Checked(name, "custom class name");
            custom.Name = custom.Id;
            entityManager.SetComponentData(player, custom);
        }

        void AnswerGenerateQuestion(ref RuntimeContentBlob content, ref CharacterGenerationState charGen, int specialization)
        {
            if (charGen.GenerateStep >= 10)
                throw new InvalidOperationException("[VVardenfell][CharGen] Generate class question overflow.");

            switch (RequireRange(specialization, 0, 2, "generate class specialization"))
            {
                case 0: charGen.GenerateCombat++; break;
                case 1: charGen.GenerateMagic++; break;
                case 2: charGen.GenerateStealth++; break;
            }

            charGen.GenerateStep++;
            if (charGen.GenerateStep == 10)
            {
                charGen.GeneratedClassId = CharacterGenerationUtility.ResolveGeneratedClassId(
                    charGen.GenerateCombat,
                    charGen.GenerateMagic,
                    charGen.GenerateStealth);
                CharacterGenerationUtility.RequireClass(ref content, charGen.GeneratedClassId);
            }
        }

        void Apply(ref SystemState systemState, ref RuntimeContentBlob content, ref CharacterGenerationState charGen)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;
            CharacterGenerationUtility.ApplyToPlayer(systemState.EntityManager, _playerQuery.GetSingletonEntity(), ref content, ref charGen);
        }

        void HandleDialogDone(
            EntityManager entityManager,
            Entity charGenEntity,
            ref CharacterGenerationState charGen,
            ref RuntimeShellState shell,
            CharacterGenerationStage currentStage,
            CharacterGenerationMenu nextMenu)
        {
            byte previousStage = charGen.Stage;
            if (previousStage == (byte)CharacterGenerationStage.ReviewNext)
            {
                OpenRequestedMenu(entityManager, charGenEntity, ref charGen, ref shell, CharacterGenerationMenu.Review);
                return;
            }

            if (previousStage >= (byte)currentStage)
            {
                OpenRequestedMenu(entityManager, charGenEntity, ref charGen, ref shell, nextMenu);
                return;
            }

            charGen.Stage = (byte)currentStage;
            CharacterGenerationUtility.Close(ref charGen);
            RuntimeShellStateUtility.CloseCharacterGeneration(ref shell);
            EnsureCharGenStage(entityManager, charGenEntity, in charGen);
        }

        static void EnsureSelectedBodyParts(ref RuntimeContentBlob content, ref CharacterGenerationState charGen)
        {
            bool male = charGen.Male != 0;
            if (!IsSelectedBodyPartValid(ref content, charGen.RaceId, male, ActorBodyPartMeshPart.Head, charGen.HeadId))
                charGen.HeadId = CharacterGenerationUtility.RequireFirstPlayableBodyPartId(ref content, charGen.RaceId, male, ActorBodyPartMeshPart.Head);
            if (!IsSelectedBodyPartValid(ref content, charGen.RaceId, male, ActorBodyPartMeshPart.Hair, charGen.HairId))
                charGen.HairId = CharacterGenerationUtility.RequireFirstPlayableBodyPartId(ref content, charGen.RaceId, male, ActorBodyPartMeshPart.Hair);
        }

        static void ResetSelectedBodyParts(ref RuntimeContentBlob content, ref CharacterGenerationState charGen)
        {
            bool male = charGen.Male != 0;
            charGen.HeadId = CharacterGenerationUtility.RequireFirstPlayableBodyPartId(ref content, charGen.RaceId, male, ActorBodyPartMeshPart.Head);
            charGen.HairId = CharacterGenerationUtility.RequireFirstPlayableBodyPartId(ref content, charGen.RaceId, male, ActorBodyPartMeshPart.Hair);
        }

        static void RequirePlayableBodyPart(
            ref RuntimeContentBlob content,
            FixedString64Bytes raceId,
            bool male,
            ActorBodyPartMeshPart part,
            FixedString64Bytes selectedId)
        {
            if (!IsSelectedBodyPartValid(ref content, raceId, male, part, selectedId))
                throw new InvalidOperationException($"[VVardenfell][CharGen] Body part '{selectedId}' is not a playable {part} for race '{raceId}' and {(male ? "male" : "female")} sex.");
        }

        static bool IsSelectedBodyPartValid(
            ref RuntimeContentBlob content,
            FixedString64Bytes raceId,
            bool male,
            ActorBodyPartMeshPart part,
            FixedString64Bytes selectedId)
        {
            if (selectedId.IsEmpty)
                return false;
            if (!RuntimeContentBlobUtility.TryGetActorBodyPartHandleByIdHash(ref content, RuntimeContentStableHash.HashId(selectedId.ToString()), out var handle)
                || !handle.IsValid
                || (uint)handle.Index >= (uint)content.ActorBodyParts.Length)
            {
                return false;
            }

            ref RuntimeActorBodyPartDefBlob bodyPart = ref content.ActorBodyParts[handle.Index];
            return CharacterGenerationUtility.IsPlayableBodyPart(ref bodyPart, raceId, male, part);
        }

        delegate void CustomClassMutation(ref PlayerCustomClass customClass);

        void MutateCustomClass(ref SystemState systemState, CustomClassMutation mutation)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][CharGen] Cannot mutate custom class without a live player.");

            Entity player = _playerQuery.GetSingletonEntity();
            if (!systemState.EntityManager.HasComponent<PlayerCustomClass>(player))
                throw new InvalidOperationException("[VVardenfell][CharGen] Player has no custom class component.");

            var customClass = systemState.EntityManager.GetComponentData<PlayerCustomClass>(player);
            mutation(ref customClass);
            systemState.EntityManager.SetComponentData(player, customClass);
        }

        void ValidateCustomClass(ref SystemState systemState)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][CharGen] Cannot validate custom class without a live player.");
            var custom = systemState.EntityManager.GetComponentData<PlayerCustomClass>(_playerQuery.GetSingletonEntity());
            if (custom.Active == 0 || custom.Name.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][CharGen] Custom class requires a name.");
            Span<int> skills = stackalloc int[10];
            skills[0] = custom.MajorSkill0;
            skills[1] = custom.MajorSkill1;
            skills[2] = custom.MajorSkill2;
            skills[3] = custom.MajorSkill3;
            skills[4] = custom.MajorSkill4;
            skills[5] = custom.MinorSkill0;
            skills[6] = custom.MinorSkill1;
            skills[7] = custom.MinorSkill2;
            skills[8] = custom.MinorSkill3;
            skills[9] = custom.MinorSkill4;
            for (int i = 0; i < skills.Length; i++)
            {
                RequireRange(skills[i], 0, 26, "custom class skill");
                for (int j = i + 1; j < skills.Length; j++)
                {
                    if (skills[i] == skills[j])
                        throw new InvalidOperationException($"[VVardenfell][CharGen] Custom class duplicates skill index {skills[i]}.");
                }
            }
        }

        bool IsCustomClassComplete(ref SystemState systemState)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return false;
            var custom = systemState.EntityManager.GetComponentData<PlayerCustomClass>(_playerQuery.GetSingletonEntity());
            return custom.Active != 0 && !custom.Name.IsEmpty;
        }

        static void SetCustomAttribute(ref PlayerCustomClass custom, int slot, int attribute)
        {
            switch (slot)
            {
                case 0:
                    if (custom.FavoredAttribute1 == attribute)
                        custom.FavoredAttribute1 = custom.FavoredAttribute0;
                    custom.FavoredAttribute0 = attribute;
                    break;
                case 1:
                    if (custom.FavoredAttribute0 == attribute)
                        custom.FavoredAttribute0 = custom.FavoredAttribute1;
                    custom.FavoredAttribute1 = attribute;
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][CharGen] Invalid custom class attribute slot {slot}.");
            }
        }

        static void SetCustomSkill(ref PlayerCustomClass custom, int slot, int skill)
        {
            RequireRange(slot, 0, 9, "custom class skill slot");
            int previous = GetCustomSkill(custom, slot);
            for (int i = 0; i < 10; i++)
            {
                if (i != slot && GetCustomSkill(custom, i) == skill)
                    SetCustomSkillRaw(ref custom, i, previous);
            }

            SetCustomSkillRaw(ref custom, slot, skill);
        }

        static int GetCustomSkill(in PlayerCustomClass custom, int slot)
            => slot switch
            {
                0 => custom.MajorSkill0,
                1 => custom.MajorSkill1,
                2 => custom.MajorSkill2,
                3 => custom.MajorSkill3,
                4 => custom.MajorSkill4,
                5 => custom.MinorSkill0,
                6 => custom.MinorSkill1,
                7 => custom.MinorSkill2,
                8 => custom.MinorSkill3,
                9 => custom.MinorSkill4,
                _ => throw new InvalidOperationException($"[VVardenfell][CharGen] Invalid custom class skill slot {slot}."),
            };

        static void SetCustomSkillRaw(ref PlayerCustomClass custom, int slot, int skill)
        {
            switch (slot)
            {
                case 0: custom.MajorSkill0 = skill; break;
                case 1: custom.MajorSkill1 = skill; break;
                case 2: custom.MajorSkill2 = skill; break;
                case 3: custom.MajorSkill3 = skill; break;
                case 4: custom.MajorSkill4 = skill; break;
                case 5: custom.MinorSkill0 = skill; break;
                case 6: custom.MinorSkill1 = skill; break;
                case 7: custom.MinorSkill2 = skill; break;
                case 8: custom.MinorSkill3 = skill; break;
                case 9: custom.MinorSkill4 = skill; break;
                default: throw new InvalidOperationException($"[VVardenfell][CharGen] Invalid custom class skill slot {slot}.");
            }
        }

        static void RequireText(FixedString512Bytes value, string label)
        {
            if (string.IsNullOrWhiteSpace(value.ToString()))
                throw new InvalidOperationException($"[VVardenfell][CharGen] {label} cannot be empty.");
        }

        static void RequireId(FixedString64Bytes value, string label)
        {
            if (value.IsEmpty || string.IsNullOrWhiteSpace(value.ToString()))
                throw new InvalidOperationException($"[VVardenfell][CharGen] {label} id cannot be empty.");
        }

        static FixedString64Bytes ToFixed64(string value)
        {
            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(value ?? string.Empty);
            return result;
        }

        static FixedString64Bytes ToFixed64Checked(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"[VVardenfell][CharGen] {label} cannot be empty.");

            var result = default(FixedString64Bytes);
            CopyError error = result.CopyFrom(value);
            if (error != CopyError.None)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][CharGen] {label} exceeds FixedString64Bytes capacity of {FixedString64Bytes.UTF8MaxLengthInBytes} UTF-8 bytes.");
            }

            return result;
        }

        static void EnsureCharGenStage(EntityManager entityManager, Entity charGenEntity, in CharacterGenerationState charGen)
        {
            var stage = new CharGenStage
            {
                Stage = charGen.Stage,
                Menu = charGen.CurrentMenu,
                Finalized = charGen.Finalized,
            };

            if (entityManager.HasComponent<CharGenStage>(charGenEntity))
                entityManager.SetComponentData(charGenEntity, stage);
            else
                entityManager.AddComponentData(charGenEntity, stage);
        }

        static void RemoveCharGenStage(EntityManager entityManager, Entity charGenEntity)
        {
            if (entityManager.HasComponent<CharGenStage>(charGenEntity))
                entityManager.RemoveComponent<CharGenStage>(charGenEntity);
        }

        static void ApplyOpenMenuStageParity(ref CharacterGenerationState charGen, CharacterGenerationMenu menu)
        {
            switch (menu)
            {
                case CharacterGenerationMenu.Race:
                    RaiseStage(ref charGen, CharacterGenerationStage.NameChosen);
                    break;
                case CharacterGenerationMenu.ClassChoice:
                case CharacterGenerationMenu.ClassPick:
                case CharacterGenerationMenu.ClassCreate:
                case CharacterGenerationMenu.ClassGenerateQuestion:
                case CharacterGenerationMenu.ClassGenerateResult:
                    RaiseStage(ref charGen, CharacterGenerationStage.RaceChosen);
                    break;
                case CharacterGenerationMenu.Birth:
                    RaiseStage(ref charGen, CharacterGenerationStage.ClassChosen);
                    break;
                case CharacterGenerationMenu.Review:
                    RaiseStage(ref charGen, CharacterGenerationStage.BirthSignChosen);
                    break;
            }
        }

        static void RaiseStage(ref CharacterGenerationState charGen, CharacterGenerationStage stage)
        {
            if (charGen.Stage < (byte)stage)
                charGen.Stage = (byte)stage;
        }

        static int RequireRange(int value, int min, int max, string label)
        {
            if (value < min || value > max)
                throw new InvalidOperationException($"[VVardenfell][CharGen] {label} {value} outside [{min}, {max}].");
            return value;
        }
    }
}
