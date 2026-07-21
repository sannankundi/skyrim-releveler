using System.Collections.Generic;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.WPF.Reflection.Attributes;

namespace SkyrimReleveler
{
    public class BonusKeyword
    {
        [Tooltip("If an NPC's EditorID contains this string (case-insensitive), apply the bonus percent to their computed level.")]
        public string Keyword { get; set; } = "";

        [Tooltip("Percentage bonus added to the NPC's computed level. e.g. 25 = +25%. Stacks with GlobalOffset.")]
        public float BonusPercent { get; set; } = 25f;
    }

    public class FollowerEntry
    {
        [Tooltip("If an NPC's EditorID contains this string (case-insensitive), it is treated as a follower and gets unlimited player scaling.")]
        public string Key { get; set; } = "";

        [Tooltip("If the EditorID also contains any of these strings, the NPC is NOT treated as a follower even if Key matched.")]
        public List<string> ForbiddenKeys { get; set; } = new();
    }

    public class FollowerList
    {
        [Tooltip("Custom follower entries for mod-added companions not in vanilla follower factions.")]
        public List<FollowerEntry> Followers { get; set; } = new();
    }

    public class Settings
    {
        [Tooltip("A flat level value added or subtracted from every NPC's final computed level. Useful for making the whole world harder or easier. Default is 0.")]
        public int GlobalOffset { get; set; } = 0;

        [Tooltip("Prints detailed per-NPC level computation info to the Synthesis log. Useful for diagnosing unexpected levels. Leave off for normal runs.")]
        public bool PrintDebugOutput { get; set; } = false;

        [Tooltip("If enabled, overwrites all deployed data files (named_npcs.json, importanceWeights.json, etc.) with the bundled defaults on the next run. Turn on after updating the patcher to push new defaults. Automatically resets to false after running.")]
        public bool ForceReseedData { get; set; } = false;

        [Tooltip("Dumps all race EditorIDs from the full load order to the log at startup, then continues normally. Use this after adding new mods to check for new races that may need classifier attention. Safe to leave off during normal runs.")]
        public bool DumpRaces { get; set; } = false;

        // -------------------------------------------------------------------------
        // Follower scaling
        // -------------------------------------------------------------------------
        [Tooltip("If enabled, followers (vanilla follower factions + custom list) scale 1:1 with the player with no level cap. They will never become weaker than the player. Followers are excluded from NPC releveling.")]
        public bool ScaleFollowers { get; set; } = true;

        [Tooltip("The assumed level used to calculate how many perks followers receive. Since followers scale with the player instead of having a fixed level, this value is used as a stand-in for perk budget. Default is 100. Set to 0 to give followers no perks.")]
        public int FollowerPerkLevel { get; set; } = 500;

        // -------------------------------------------------------------------------
        // NPC skill redistribution
        // -------------------------------------------------------------------------
        [Tooltip("Skill points given to each NPC per level, distributed across their skill trees based on class weights. A level 50 NPC with 2.5 gets 125 total points. Set to 0 to leave NPC skills at their original values.")]
        public float NPCSkillsPerLevel { get; set; } = 0.7f;

        [Tooltip("The maximum level any individual NPC skill can reach. Raising this above 100 allows very high level NPCs to exceed the normal cap in their primary skills.")]
        public byte NPCMaxSkillLevel { get; set; } = 100;

        // -------------------------------------------------------------------------
        // NPC class rebuild
        // -------------------------------------------------------------------------
        [Tooltip("If enabled, each NPC gets a new class record generated from their actual weapons, armor, and spells. This ensures skill points go to skills they actually use. Unique vendor/crafter NPCs are excluded.")]
        public bool RebuildNPCClasses { get; set; } = true;

        // -------------------------------------------------------------------------
        // NPC perk distribution
        // -------------------------------------------------------------------------
        [Tooltip("Perk points given per NPC level, spent in their skill trees respecting skill requirements and perk prerequisites. A level 50 NPC with 0.25 gets ~12 perks. Set to 0 to disable perk distribution entirely.")]
        public float NPCPerksPerLevel { get; set; } = 0.18f;

        [Tooltip("If enabled, vanilla perks are stripped from NPCs before new ones are distributed. Only removes perks from vanilla ESMs (Skyrim, Dawnguard, Dragonborn). Mod-added perks and racial abilities are preserved. Does not affect followers.")]
        public bool RemoveVanillaPerks { get; set; } = true;

        [Tooltip("If enabled, the crExtraDamage perks that give NPCs artificial damage multipliers are cleared so they do nothing. Disable this if you use a mod that relies on those perks for balance (e.g. Requiem, Wildcat).")]
        public bool DisableExtraDamagePerks { get; set; } = false;

        [Tooltip("NPCs whose race or own keywords include any keyword on this list will not have their perks redistributed at all. Useful for excluding undead, Dwemer constructs, or other non-humanoid enemies from perk distribution.")]
        public List<FormLink<IKeywordGetter>> PerkDistributionFilter { get; set; } = new();

        // -------------------------------------------------------------------------
        // Auto-leveling system
        // -------------------------------------------------------------------------
        [Tooltip("The value all tier ranges scale against. Tier 0 (Cosmic, Molag Bal / Daedric Prince level) maps to this ceiling. All other tiers scale proportionally below it. Default is 2000.")]
        public int TierScalingBase { get; set; } = 2000;

        [Tooltip("The NPC source level at which the above-ceiling multiplier fades to 1.0 (no bonus). NPCs at or above this level get no multiplier inflation on top of their tier range — they are already powerful enough. NPCs between the vanilla ceiling and this value get a smoothly diminishing bonus. Default is 1000.")]
        public float MultiplierFadeOutLevel { get; set; } = 1000f;

        [Tooltip("The maximum tier multiplier applied to unique NPCs whose source level is just above the vanilla ceiling for their tier. Decays logarithmically toward 1.0 as source level approaches MultiplierFadeOutLevel. Default is 4.0.")]
        public float MaxTierMultiplier { get; set; } = 4.0f;

        [Tooltip("Minimum number of NPCs that must share a faction before that faction is used as a peer group for percentile scoring. Factions with fewer members fall back to race-based peer comparison. Default is 3.")]
        public int AutoFactionMinPeers { get; set; } = 3;

        // -------------------------------------------------------------------------
        // Outlier trimming
        // -------------------------------------------------------------------------
        [Tooltip("Trims the most extreme NPC levels from peer group range discovery. 0.05 = ignore the bottom and top 5% of members. Set to 0 to use the absolute min/max.")]
        public float OutlierPercentileCutoff { get; set; } = 0.05f;

        // -------------------------------------------------------------------------
        // Name grouping (flat factions only)
        // -------------------------------------------------------------------------
        [Tooltip("Prefixes stripped from EditorIDs before extracting a stem name. Used for flat factions (e.g. all dragons at level 100) to identify distinct NPC types so they get different scatter bands. Add mod prefixes here if needed.")]
        public List<string> StripPrefixes { get; set; } = new()
        {
            // Vanilla and DLC prefixes
            "Enc", "DLC1", "DLC2", "DLC",
            // Encounter/world-event prefixes
            "WE", "dun",
            // Quest prefixes
            "MS07", "MS08", "DA13", "DA16",
            // Mod prefixes
            "CYR", "zzz", "XMD", "XJK", "000FC",
            // OGDD (Organized Gameplay - Dragon Diversity)
            "ogdd_",
        };

        [Tooltip("Suffixes stripped right-to-left from EditorIDs before extracting a stem. Combat role suffixes (Melee, Archer, Boss) and race suffixes are stripped so e.g. DragonFrostBossNord -> DragonFrost. Add mod suffixes as needed.")]
        public List<string> StripSuffixes { get; set; } = new()
        {
            // Boss variants
            "BossMagic", "BossMelee", "Boss",
            // Combat roles
            "Berserker", "Melee", "Missile", "Magic", "Ranged",
            "Tank", "Caster", "Archer", "Shield",
            // Template/spawn markers
            "Template", "Base", "Spawn",
            // Gender suffixes
            "Female", "Male", "F", "M",
            // Race suffixes
            "HighElf", "DarkElf", "WoodElf",
            "Nord", "Imperial", "Breton", "Redguard",
            "Orc", "Khajiit", "Argonian",
            // Misc
            "Captain",
        };

        [Tooltip("If the stem after stripping exactly matches one of these words (case-insensitive), the NPC is treated as ungrouped — each one forms its own group and scatters independently.")]
        public List<string> GenericStemWords { get; set; } = new()
        {
            "Creature", "Enemy", "NPC", "Actor", "Character",
            "Monster", "Humanoid", "Generic", "Common", "Standard",
        };

        [Tooltip("Short words that are still meaningful stems even though they're very brief. Checked before the normal stem extraction. e.g. Bear -> bear stem rather than being truncated further.")]
        public List<string> ShortStemExceptions { get; set; } = new()
        {
            "Orc", "Dog", "Cat", "Fox", "Rat", "Bat", "Cow", "Pig",
            "Bear", "Wolf", "Deer", "Boar", "Elk",
        };

        // -------------------------------------------------------------------------
        // Weapon / armor stat multipliers
        // -------------------------------------------------------------------------
        [Tooltip("Multiplier applied to every weapon's base damage, including unique and bound weapons. 1.0 = no change. Example: vanilla damage 10 × 2.0 = 20. Set to 1.0 to disable.")]
        public float WeaponDamageMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every heavy armor piece's armor rating. 1.0 = no change. Example: vanilla rating 100 × 1.5 = 150. Set to 1.0 to disable.")]
        public float HeavyArmorRatingMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every light armor piece's armor rating. 1.0 = no change. Example: vanilla rating 60 × 1.5 = 90. Set to 1.0 to disable.")]
        public float LightArmorRatingMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every shield's armor rating. 1.0 = no change. Example: vanilla rating 20 × 2.0 = 40. Set to 1.0 to disable.")]
        public float ShieldArmorRatingMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every ammo record's damage (arrows, bolts, etc.). 1.0 = no change. Example: vanilla damage 10 × 2.0 = 20. Set to 1.0 to disable.")]
        public float AmmoDamageMultiplier { get; set; } = 1.0f;

        // -------------------------------------------------------------------------
        // Boss keyword bonus
        // -------------------------------------------------------------------------
        [Tooltip("If enabled, NPCs whose EditorID contains a matching bonus keyword get their final level boosted by the configured percentage.")]
        public bool EnableBonusKeywords { get; set; } = true;

        [Tooltip("List of EditorID keyword -> bonus percent pairs. Boss NPCs get +25% by default. Add more entries for other special NPC types.")]
        public List<BonusKeyword> BonusKeywords { get; set; } = new()
        {
            new BonusKeyword { Keyword = "Boss",       BonusPercent = 25f },
            new BonusKeyword { Keyword = "Chief",      BonusPercent = 20f },
            new BonusKeyword { Keyword = "Warlord",    BonusPercent = 15f },
            new BonusKeyword { Keyword = "Briarheart", BonusPercent = 20f },
            new BonusKeyword { Keyword = "Deathlord",  BonusPercent = 15f },
            new BonusKeyword { Keyword = "Master",     BonusPercent = 10f },
        };
    }
}
