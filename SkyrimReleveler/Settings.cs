using System.Collections.Generic;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.WPF.Reflection.Attributes;

namespace SkyrimReleveler
{
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
        [Tooltip("Master switch for the NPC releveling system. When disabled, NPC levels are left completely untouched. All other leveling settings are ignored. Default is true.")]
        public bool EnableReleveling { get; set; } = true;

        [Tooltip("A flat level value added or subtracted from every NPC's final computed level. Useful for making the whole world harder or easier. Default is 0.")]
        public int GlobalOffset { get; set; } = 0;

        [Tooltip("Prints detailed per-NPC level computation info to the Synthesis log. Useful for diagnosing unexpected levels. Leave off for normal runs.")]
        public bool PrintDebugOutput { get; set; } = false;

        [Tooltip("If enabled, overwrites all deployed data files (named_npcs.json, etc.) with the bundled defaults on the next run. Automatically resets to false after running.")]
        public bool ForceReseedData { get; set; } = false;

        [Tooltip("Dumps all race EditorIDs from the full load order to the log at startup. Use after adding new mods to check for new races that may need classifier attention.")]
        public bool DumpRaces { get; set; } = false;

        // -------------------------------------------------------------------------
        // Follower scaling
        // -------------------------------------------------------------------------
        [Tooltip("If enabled, followers scale 1:1 with the player with no level cap. Followers are excluded from NPC releveling.")]
        public bool ScaleFollowers { get; set; } = true;

        [Tooltip("The assumed level used to calculate how many perks followers receive. Default is 500. Set to 0 to give followers no perks.")]
        public int FollowerPerkLevel { get; set; } = 500;

        // -------------------------------------------------------------------------
        // NPC skill redistribution
        // -------------------------------------------------------------------------
        [Tooltip("Skill points given to each NPC per level, distributed across their skill trees based on class weights. Set to 0 to leave NPC skills at their original values.")]
        public float NPCSkillsPerLevel { get; set; } = 0.7f;

        [Tooltip("The maximum level any individual NPC skill can reach.")]
        public byte NPCMaxSkillLevel { get; set; } = 100;

        // -------------------------------------------------------------------------
        // NPC class rebuild
        // -------------------------------------------------------------------------
        [Tooltip("If enabled, each NPC gets a new class record generated from their actual weapons, armor, and spells.")]
        public bool RebuildNPCClasses { get; set; } = true;

        // -------------------------------------------------------------------------
        // NPC perk distribution
        // -------------------------------------------------------------------------
        [Tooltip("Perk points given per NPC level, spent in their skill trees respecting skill requirements and prerequisites. Set to 0 to disable perk distribution entirely.")]
        public float NPCPerksPerLevel { get; set; } = 0.3f;

        [Tooltip("If enabled, vanilla perks are stripped from NPCs before new ones are distributed.")]
        public bool RemoveVanillaPerks { get; set; } = true;

        [Tooltip("If enabled, the crExtraDamage perks that give NPCs artificial damage multipliers are cleared.")]
        public bool DisableExtraDamagePerks { get; set; } = false;

        [Tooltip("NPCs whose race or own keywords include any keyword on this list will not have their perks redistributed.")]
        public List<FormLink<IKeywordGetter>> PerkDistributionFilter { get; set; } = new();

        // -------------------------------------------------------------------------
        // Auto-leveling system
        // -------------------------------------------------------------------------
        [Tooltip("The value all tier ranges scale against. Tier 0 (Cosmic) maps to this ceiling. All other tiers scale proportionally below it. Default is 2000.")]
        public int TierScalingBase { get; set; } = 2000;

        [Tooltip("Minimum number of NPCs that must share a faction before that faction is used as a peer group. Factions with fewer members fall back to race-based peers. Default is 3.")]
        public int FactionMinPeers { get; set; } = 3;

        [Tooltip("Boss keyword tokens. NPCs with no source level whose EditorID contains one of these are placed at the top quarter of their tier range instead of the midpoint.")]
        public List<string> BossTokens { get; set; } = new()
        {
            "boss", "chief", "master", "lord", "king", "queen",
            "elder", "ancient", "arch", "high", "warlord", "deathlord",
        };

        // -------------------------------------------------------------------------
        // Weapon / armor stat multipliers
        // -------------------------------------------------------------------------
        [Tooltip("Multiplier applied to every weapon's base damage. 1.0 = no change.")]
        public float WeaponDamageMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every heavy armor piece's armor rating. 1.0 = no change.")]
        public float HeavyArmorRatingMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every light armor piece's armor rating. 1.0 = no change.")]
        public float LightArmorRatingMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every shield's armor rating. 1.0 = no change.")]
        public float ShieldArmorRatingMultiplier { get; set; } = 1.0f;

        [Tooltip("Multiplier applied to every ammo record's damage. 1.0 = no change.")]
        public float AmmoDamageMultiplier { get; set; } = 1.0f;
    }
}
