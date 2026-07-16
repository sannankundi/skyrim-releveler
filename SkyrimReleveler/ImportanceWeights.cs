using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SkyrimReleveler
{
    public class ContradictionPair
    {
        [JsonProperty] public string SignalA { get; set; } = "";
        [JsonProperty] public string SignalB { get; set; } = "";
        [JsonProperty] public float  Weight  { get; set; } = 0.0f;
    }

    public class ImportanceWeights
    {
        // --- per-signal weights (signal disabled when 0.0) ---
        [JsonProperty] public float UniqueFlag         { get; set; } = 0.20f;
        [JsonProperty] public float EssentialFlag      { get; set; } = 0.25f;
        [JsonProperty] public float ProtectedFlag      { get; set; } = 0.10f;
        [JsonProperty] public float NoRespawnFlag      { get; set; } = 0.10f;
        [JsonProperty] public float CalcMinLevelHigh   { get; set; } = 0.15f;
        [JsonProperty] public float CalcMaxLevelHigh   { get; set; } = 0.15f;
        [JsonProperty] public float PcLevelMult        { get; set; } = 0.05f;
        [JsonProperty] public float HighFactionRank    { get; set; } = 0.15f;
        [JsonProperty] public float ManyFactions       { get; set; } = 0.10f;
        [JsonProperty] public float ManyKeywords       { get; set; } = 0.05f;
        [JsonProperty] public float HasCombatStyle     { get; set; } = 0.05f;
        [JsonProperty] public float HasScripts         { get; set; } = 0.10f;
        [JsonProperty] public float BossToken          { get; set; } = 0.15f;
        [JsonProperty] public float UniqueVoiceType    { get; set; } = 0.08f;
        [JsonProperty] public float ModOriginUnique    { get; set; } = 0.12f;
        [JsonProperty] public float BossClass          { get; set; } = 0.35f;
        [JsonProperty] public float ActorTypeKeyword   { get; set; } = 0.20f;
        [JsonProperty] public float HighSkillValue     { get; set; } = 0.18f;
        [JsonProperty] public float HighVanillaPerks   { get; set; } = 0.12f;
        [JsonProperty] public float AbsoluteEquipment  { get; set; } = 0.15f;
        [JsonProperty] public float RelativeEquipment  { get; set; } = 0.22f;
        [JsonProperty] public float UniqueItem         { get; set; } = 0.25f;

        // --- numeric thresholds ---
        [JsonProperty] public int CalcMinLevelThreshold  { get; set; } = 50;
        [JsonProperty] public int CalcMaxLevelThreshold  { get; set; } = 100;
        [JsonProperty] public int FactionRankThreshold   { get; set; } = 2;
        [JsonProperty] public int MinFactionCount        { get; set; } = 3;
        [JsonProperty] public int MinKeywordCount        { get; set; } = 5;
        [JsonProperty] public int HighSkillThreshold     { get; set; } = 75;
        [JsonProperty] public int HighPerkCountThreshold { get; set; } = 5;
        [JsonProperty] public float AbsoluteEquipThreshold { get; set; } = 0.5f;
        [JsonProperty] public float RelativeEquipRatio   { get; set; } = 1.5f;

        // --- review thresholds ---
        [JsonProperty] public float LowConfidenceThreshold { get; set; } = 0.5f;
        [JsonProperty] public float FloorDeltaThreshold    { get; set; } = 50.0f;

        // --- lists ---
        [JsonProperty] public List<string> BossTokens { get; set; } = new()
        {
            "boss", "chief", "master", "lord",
            "king", "queen", "elder", "ancient", "arch", "high"
        };

        [JsonProperty] public List<string> GenericVoiceTypes { get; set; } = new()
        {
            "MaleEvenToned", "FemaleEvenToned",
            "MaleGuard", "FemaleCommoner", "MaleCommoner",
            "MaleBrute", "MaleNord", "FemaleNord"
        };

        // Boss-tier class EditorID tokens — NPC class matches one of these → strong importance signal
        [JsonProperty] public List<string> BossClassTokens { get; set; } = new()
        {
            "DragonPriest", "Dremora", "EbonyWarrior", "Miraak", "Karstaag",
            "Haknir", "Vyrthur", "WerewolfBoss", "Giant", "Dragon",
            "Atronach", "Draugr", "Falmer", "Hagraven", "Spriggan",
            "Vampire", "Gargoyle", "Lurker", "Seeker", "AshGuardian",
            "Ancient", "NordHero", "MQAncient", "Nightingale", "Blade",
            "Forsworn", "PenitusOculatus", "Penitus",
            "Centurion", "DwarvenSphere", "DwarvenSpider", "IceWraith", "Chaurus",
            "Mammoth", "Horker", "FrostbiteSpider", "MudCrab", "Werewolf",
            "Predator", "Bear", "Vigilant", "Riekling",
            "DLC2Neloth", "DLC2dunKolbjorn", "DLC1CClass", "DLC2EbonyWarrior",
            "MQLabyrinthian", "EncClassDragonPriest", "EncClassDragon"
        };

        // Child/civilian class EditorIDs that suppress importance score (exact match, overridden by boss class)
        [JsonProperty] public List<string> ChildClassTokens { get; set; } = new()
        {
            "Child"
        };

        // Actor type keyword tokens that indicate a boss/spirit NPC
        [JsonProperty] public List<string> BossActorTypeKeywords { get; set; } = new()
        {
            "ActorTypeDLC1Boss", "ActorTypeSpirit"
        };

        // Item EditorID substrings that indicate a unique/artifact item
        [JsonProperty] public List<string> UniqueItemKeywords { get; set; } = new()
        {
            "Volendrung","Wuuthrad","EbonyBlade","MaceofMolagBal","SkullofdCorruption",
            "Dawnbreaker","Mehrunes","AzurasStarBlack","AzurasStar","Nightingale",
            "Chillrend","DragonboneMace","DragonboneSword","DragonboneGreatSword",
            "DragonboneWarAxe","DragonboneBattleAxe","DragonboneDagger","DragonboneBow",
            "Harkon","HaknirDeathBrand","AhzidalArmor","StalhrimBoss",
            "DLC2_Stalhrim","MolagBal","Auriel","EbonyMailCuirass",
            "GauldurAmulet","WhiterunSword","Windshear","Targe",
            "Longhammer","ValdrLucky","PaarthurnaxMedallion","HircinesRing",
            "SaviorHide","Namira","Clavicus","SkaalArmor","Karstaag"
        };

        // --- contradiction pairs ---
        [JsonProperty] public List<ContradictionPair> ContradictionPairs { get; set; } = new()
        {
            new ContradictionPair { SignalA = "UniqueFlag",    SignalB = "NoRespawnFlag",      Weight = 0.3f },
            new ContradictionPair { SignalA = "EssentialFlag", SignalB = "CalcMaxLevelHigh",   Weight = 0.2f },
        };

        /// <summary>
        /// Clamps all weight fields: negative, NaN, or infinite values are replaced with 0.0
        /// and a warning is logged for each offending field.
        /// </summary>
        public void Sanitize()
        {
            SanitizeField(nameof(UniqueFlag),       UniqueFlag,       v => UniqueFlag       = v);
            SanitizeField(nameof(EssentialFlag),    EssentialFlag,    v => EssentialFlag    = v);
            SanitizeField(nameof(ProtectedFlag),    ProtectedFlag,    v => ProtectedFlag    = v);
            SanitizeField(nameof(NoRespawnFlag),    NoRespawnFlag,    v => NoRespawnFlag    = v);
            SanitizeField(nameof(CalcMinLevelHigh), CalcMinLevelHigh, v => CalcMinLevelHigh = v);
            SanitizeField(nameof(CalcMaxLevelHigh), CalcMaxLevelHigh, v => CalcMaxLevelHigh = v);
            SanitizeField(nameof(PcLevelMult),      PcLevelMult,      v => PcLevelMult      = v);
            SanitizeField(nameof(HighFactionRank),  HighFactionRank,  v => HighFactionRank  = v);
            SanitizeField(nameof(ManyFactions),     ManyFactions,     v => ManyFactions     = v);
            SanitizeField(nameof(ManyKeywords),     ManyKeywords,     v => ManyKeywords     = v);
            SanitizeField(nameof(HasCombatStyle),   HasCombatStyle,   v => HasCombatStyle   = v);
            SanitizeField(nameof(HasScripts),       HasScripts,       v => HasScripts       = v);
            SanitizeField(nameof(BossToken),        BossToken,        v => BossToken        = v);
            SanitizeField(nameof(UniqueVoiceType),  UniqueVoiceType,  v => UniqueVoiceType  = v);
            SanitizeField(nameof(ModOriginUnique),  ModOriginUnique,  v => ModOriginUnique  = v);
            SanitizeField(nameof(BossClass),        BossClass,        v => BossClass        = v);
            SanitizeField(nameof(ActorTypeKeyword), ActorTypeKeyword, v => ActorTypeKeyword = v);
            SanitizeField(nameof(HighSkillValue),   HighSkillValue,   v => HighSkillValue   = v);
            SanitizeField(nameof(HighVanillaPerks), HighVanillaPerks, v => HighVanillaPerks = v);
            SanitizeField(nameof(AbsoluteEquipment),AbsoluteEquipment,v => AbsoluteEquipment= v);
            SanitizeField(nameof(RelativeEquipment),RelativeEquipment,v => RelativeEquipment= v);
            SanitizeField(nameof(UniqueItem),       UniqueItem,       v => UniqueItem       = v);
        }

        private static void SanitizeField(string name, float value, Action<float> setter)
        {
            if (value < 0f || !float.IsFinite(value))
            {
                Console.WriteLine($"  [WARNING] ImportanceWeights: field '{name}' has invalid value {value}; clamping to 0.0");
                setter(0f);
            }
        }
    }
}
