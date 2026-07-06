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

        // -------------------------------------------------------------------------
        // Follower scaling
        // -------------------------------------------------------------------------
        [Tooltip("If enabled, followers (vanilla follower factions + custom list) scale 1:1 with the player with no level cap. They will never become weaker than the player. Followers are excluded from NPC releveling.")]
        public bool ScaleFollowers { get; set; } = true;

        // -------------------------------------------------------------------------
        // NPC skill redistribution
        // -------------------------------------------------------------------------
        [Tooltip("Skill points given to each NPC per level, distributed across their skill trees based on class weights. A level 50 NPC with 2.5 gets 125 total points. Set to 0 to leave NPC skills at their original values.")]
        public float NPCSkillsPerLevel { get; set; } = 2.5f;

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
        public float NPCPerksPerLevel { get; set; } = 0.25f;

        [Tooltip("If enabled, vanilla perks are stripped from NPCs before new ones are distributed. Only removes perks from vanilla ESMs (Skyrim, Dawnguard, Dragonborn). Mod-added perks and racial abilities are preserved. Does not affect followers.")]
        public bool RemoveVanillaPerks { get; set; } = true;

        [Tooltip("If enabled, the crExtraDamage perks that give NPCs artificial damage multipliers are cleared so they do nothing. Disable this if you use a mod that relies on those perks for balance (e.g. Requiem, Wildcat).")]
        public bool DisableExtraDamagePerks { get; set; } = false;

        [Tooltip("NPCs whose race or own keywords include any keyword on this list will not have their perks redistributed at all. Useful for excluding undead, Dwemer constructs, or other non-humanoid enemies from perk distribution.")]
        public List<FormLink<IKeywordGetter>> PerkDistributionFilter { get; set; } = new();

        // -------------------------------------------------------------------------
        // Auto-leveling system
        // -------------------------------------------------------------------------
        [Tooltip("The absolute level ceiling assigned to the top of Tier 0 (Cosmic). Molag Bal, Jyggalag, and similar Daedric Prince-level entities map to this value. All tier ranges scale proportionally. Default is 1000.")]
        public int WorldMaxLevel { get; set; } = 1000;

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
            "Tank", "Caster", "Archer", "Shield", "Warrior",
            "Assassin", "Rogue", "Mage", "Necromancer",
            // Template/spawn markers
            "Template", "Base", "Spawn", "NoScript", "Leveled",
            // Gender suffixes
            "Female", "Male",
            // Race suffixes
            "HighElf", "DarkElf", "WoodElf",
            "Nord", "Imperial", "Breton", "Redguard",
            "Orc", "Khajiit", "Argonian",
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
        // Boss keyword bonus
        // -------------------------------------------------------------------------
        [Tooltip("If enabled, NPCs whose EditorID contains a matching bonus keyword get their final level boosted by the configured percentage.")]
        public bool EnableBonusKeywords { get; set; } = true;

        [Tooltip("List of EditorID keyword -> bonus percent pairs. Boss NPCs get +25% by default. Add more entries for other special NPC types.")]
        public List<BonusKeyword> BonusKeywords { get; set; } = new()
        {
            new BonusKeyword { Keyword = "Boss",      BonusPercent = 25f },
            new BonusKeyword { Keyword = "Chief",     BonusPercent = 20f },
            new BonusKeyword { Keyword = "Warlord",   BonusPercent = 15f },
            new BonusKeyword { Keyword = "Briarheart", BonusPercent = 20f },
            new BonusKeyword { Keyword = "Deathlord", BonusPercent = 15f },
            new BonusKeyword { Keyword = "Master",    BonusPercent = 10f },
        };
    }
}
