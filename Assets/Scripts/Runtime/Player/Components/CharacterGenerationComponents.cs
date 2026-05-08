using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public enum CharacterGenerationMenu : byte
    {
        None = 0,
        Name = 1,
        Race = 2,
        ClassChoice = 3,
        ClassPick = 4,
        ClassCreate = 5,
        ClassGenerateQuestion = 6,
        ClassGenerateResult = 7,
        Birth = 8,
        Review = 9,
    }

    public enum CharacterGenerationStage : byte
    {
        NotStarted = 0,
        NameChosen = 1,
        RaceChosen = 2,
        ClassChosen = 3,
        BirthSignChosen = 4,
        ReviewBack = 5,
        ReviewNext = 6,
    }

    public enum CharacterGenerationAction : byte
    {
        None = 0,
        OpenMenu = 1,
        Back = 2,
        ChooseName = 3,
        ChooseRace = 4,
        AdjustGender = 5,
        AdjustHead = 6,
        AdjustHair = 7,
        ChooseClassChoice = 8,
        PickClass = 9,
        SetCustomClassName = 10,
        SetCustomClassDescription = 11,
        SetCustomClassSpecialization = 12,
        SetCustomClassAttribute = 13,
        SetCustomClassSkill = 14,
        AcceptCustomClass = 15,
        AnswerGenerateQuestion = 16,
        AcceptGeneratedClass = 17,
        ChooseBirthsign = 18,
        ReviewOpenName = 19,
        ReviewOpenRace = 20,
        ReviewOpenClass = 21,
        ReviewOpenBirthsign = 22,
        AcceptReview = 23,
    }

    public enum CharacterGenerationClassChoice : byte
    {
        Generate = 0,
        Pick = 1,
        Create = 2,
        Back = 3,
    }

    public struct CharacterGenerationState : IComponentData
    {
        public byte Initialized;
        public byte Finalized;
        public byte CurrentMenu;
        public byte Stage;
        public byte Male;
        public byte CustomClassActive;
        public byte GenerateStep;
        public byte GenerateCombat;
        public byte GenerateMagic;
        public byte GenerateStealth;
        public uint GenerateRandomState;
        public FixedString64Bytes CharacterName;
        public FixedString64Bytes RaceId;
        public FixedString64Bytes HeadId;
        public FixedString64Bytes HairId;
        public FixedString64Bytes ClassId;
        public FixedString64Bytes BirthsignId;
        public FixedString64Bytes PendingBirthsignId;
        public FixedString64Bytes GeneratedClassId;
    }

    public struct CharGenStage : IComponentData
    {
        public byte Stage;
        public byte Menu;
        public byte Finalized;
        public int GlobalState;
    }

    public struct CharacterGenerationRequest : IComponentData
    {
        public byte Pending;
        public byte Action;
        public byte Menu;
        public byte Byte0;
        public byte Byte1;
        public int Int0;
        public int Int1;
        public FixedString512Bytes Text;
        public FixedString64Bytes Id;
    }

    public struct PlayerRaceAppearance : IComponentData
    {
        public FixedString64Bytes RaceId;
        public FixedString64Bytes HeadId;
        public FixedString64Bytes HairId;
        public byte Male;
        public byte Dirty;
    }

    public struct ActorRuntimeAppearance : IComponentData
    {
        public FixedString64Bytes RaceId;
        public FixedString64Bytes HeadId;
        public FixedString64Bytes HairId;
        public byte Male;
    }

    public struct PlayerCustomClass : IComponentData
    {
        public byte Active;
        public FixedString64Bytes Id;
        public FixedString64Bytes Name;
        public FixedString512Bytes Description;
        public int Specialization;
        public int FavoredAttribute0;
        public int FavoredAttribute1;
        public int MajorSkill0;
        public int MajorSkill1;
        public int MajorSkill2;
        public int MajorSkill3;
        public int MajorSkill4;
        public int MinorSkill0;
        public int MinorSkill1;
        public int MinorSkill2;
        public int MinorSkill3;
        public int MinorSkill4;
    }
}
