using System;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class CharacterGenerationViewModel
    {
        public CharacterGenerationMenuView Menu;
        public string Name;
        public string NameLabel;
        public string NameButtonText;
        public string NameEmptyMessage;
        public string RaceId;
        public string RaceName;
        public string ClassId;
        public string ClassName;
        public string BirthsignId;
        public string BirthsignName;
        public string RaceTooltipText;
        public string ClassTooltipText;
        public string BirthsignTooltipText;
        public bool Male;
        public string BackText;
        public string OkText;
        public string NextText;
        public string DoneText;
        public string RaceAppearanceLabel;
        public string RaceGenderLabel;
        public string RaceFaceLabel;
        public string RaceHairLabel;
        public string RaceListLabel;
        public string RaceSkillBonusLabel;
        public string RaceSpecialsLabel;
        public string RaceBackButtonText;
        public string RaceOkButtonText;
        public int HeadIndex;
        public int HairIndex;
        public CharacterGenerationRaceSkillBonusViewModel[] RaceSkillBonuses = Array.Empty<CharacterGenerationRaceSkillBonusViewModel>();
        public CharacterGenerationRacePowerViewModel[] RacePowers = Array.Empty<CharacterGenerationRacePowerViewModel>();
        public byte GenerateStep;
        public string GeneratedClassName;
        public string ClassChoiceGenerateText;
        public string ClassChoicePickText;
        public string ClassChoiceCreateText;
        public string ClassSpecializationLabel;
        public string ClassFavoredAttributesLabel;
        public string ClassMajorSkillsLabel;
        public string ClassMinorSkillsLabel;
        public string SkillClassMajorLabel;
        public string SkillClassMinorLabel;
        public string SkillClassMiscLabel;
        public string ClassOkText;
        public string ClassDescriptionButtonText;
        public string CustomClassNameLabel;
        public string CustomClassDefaultName;
        public string GeneratedClassReflectText;
        public string GeneratedClassBackText;
        public string GeneratedClassOkText;
        public string BirthsignAbilitiesLabel;
        public string BirthsignPowersLabel;
        public string BirthsignSpellsLabel;
        public string BirthsignOkText;
        public string ReviewNameLabel;
        public string ReviewRaceLabel;
        public string ReviewClassLabel;
        public string ReviewBirthsignLabel;
        public string ReviewHealthLabel;
        public string ReviewMagickaLabel;
        public string ReviewFatigueLabel;
        public string ReviewAbilitiesLabel;
        public string ReviewPowersLabel;
        public string ReviewSpellsLabel;
        public CharacterGenerationClassDetailViewModel SelectedClass;
        public CharacterGenerationClassDetailViewModel GeneratedClass;
        public CharacterGenerationBirthsignDetailViewModel SelectedBirthsign;
        public CharacterGenerationChoiceViewModel[] Races = Array.Empty<CharacterGenerationChoiceViewModel>();
        public CharacterGenerationChoiceViewModel[] Classes = Array.Empty<CharacterGenerationChoiceViewModel>();
        public CharacterGenerationChoiceViewModel[] Birthsigns = Array.Empty<CharacterGenerationChoiceViewModel>();
        public CharacterGenerationChoiceViewModel[] Heads = Array.Empty<CharacterGenerationChoiceViewModel>();
        public CharacterGenerationChoiceViewModel[] Hairs = Array.Empty<CharacterGenerationChoiceViewModel>();
        public CharacterGenerationQuestionViewModel GenerateQuestion;
        public CharacterGenerationCustomClassViewModel CustomClass;
        public CharacterGenerationReviewSpellRowsViewModel ReviewSpells;
        public StatsWindowViewModel ReviewStats;
    }

    public enum CharacterGenerationMenuView
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

    public sealed class CharacterGenerationChoiceViewModel
    {
        public string Id;
        public string Name;
        public string Description;
        public string TooltipText;
        public bool Selected;
        public int IntValue;
    }

    public sealed class CharacterGenerationQuestionViewModel
    {
        public string Question;
        public CharacterGenerationChoiceViewModel[] Answers = Array.Empty<CharacterGenerationChoiceViewModel>();
    }

    public sealed class CharacterGenerationRaceSkillBonusViewModel
    {
        public string SkillName;
        public int Bonus;
        public string TooltipText;
    }

    public sealed class CharacterGenerationRacePowerViewModel
    {
        public string Id;
        public string Name;
        public string TooltipText;
    }

    public sealed class CharacterGenerationCustomClassViewModel
    {
        public string Name;
        public string Description;
        public int Specialization;
        public string SpecializationTooltipText;
        public int FavoredAttribute0;
        public int FavoredAttribute1;
        public string FavoredAttribute0TooltipText;
        public string FavoredAttribute1TooltipText;
        public int[] MajorSkills = Array.Empty<int>();
        public string[] MajorSkillTooltips = Array.Empty<string>();
        public int[] MinorSkills = Array.Empty<int>();
        public string[] MinorSkillTooltips = Array.Empty<string>();
    }

    public sealed class CharacterGenerationClassDetailViewModel
    {
        public string Id;
        public string Name;
        public string Description;
        public string ImagePath;
        public string SpecializationName;
        public string SpecializationTooltipText;
        public string[] FavoredAttributes = Array.Empty<string>();
        public string[] FavoredAttributeTooltips = Array.Empty<string>();
        public string[] MajorSkills = Array.Empty<string>();
        public string[] MajorSkillTooltips = Array.Empty<string>();
        public string[] MinorSkills = Array.Empty<string>();
        public string[] MinorSkillTooltips = Array.Empty<string>();
        public string TooltipText;
    }

    public sealed class CharacterGenerationBirthsignDetailViewModel
    {
        public string Id;
        public string Name;
        public string Description;
        public string ImagePath;
        public CharacterGenerationSpellRowViewModel[] Abilities = Array.Empty<CharacterGenerationSpellRowViewModel>();
        public CharacterGenerationSpellRowViewModel[] Powers = Array.Empty<CharacterGenerationSpellRowViewModel>();
        public CharacterGenerationSpellRowViewModel[] Spells = Array.Empty<CharacterGenerationSpellRowViewModel>();
    }

    public sealed class CharacterGenerationSpellRowViewModel
    {
        public string Id;
        public string Name;
        public int Type;
        public RuntimeSpellTooltipViewModel SpellTooltip;
    }

    public sealed class CharacterGenerationReviewSpellRowsViewModel
    {
        public CharacterGenerationSpellRowViewModel[] Abilities = Array.Empty<CharacterGenerationSpellRowViewModel>();
        public CharacterGenerationSpellRowViewModel[] Powers = Array.Empty<CharacterGenerationSpellRowViewModel>();
        public CharacterGenerationSpellRowViewModel[] Spells = Array.Empty<CharacterGenerationSpellRowViewModel>();
    }
}
