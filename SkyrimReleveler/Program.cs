using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json;
using Noggog;
using System.Collections.Immutable;
using System.IO;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SkyrimReleveler
{
    // -------------------------------------------------------------------------
    // Data types for JSON config files
    // -------------------------------------------------------------------------
    public class ExcludedList
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
    }

    public class RaceEntry
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
        [JsonProperty] public short LevelModifierAdd { get; set; } = 0;
        [JsonProperty] public float LevelModifierMult { get; set; } = 1f;
    }

    public class RaceModifierList
    {
        [JsonProperty] public List<RaceEntry> Data { get; set; } = new();
    }

    // -------------------------------------------------------------------------
    // Tier system
    // -------------------------------------------------------------------------
    public static class TierSystem
    {
        public const int Count = 15;

        // Base ranges at WorldMaxLevel = 1000. Scaled proportionally at runtime.
        private static readonly (int Min, int Max)[] BaseRanges = new (int, int)[]
        {
            (800, 1000), // 0  Cosmic
            (600,  800), // 1  World-ender
            (350,  600), // 2  Dragon
            (250,  450), // 3  Ancient Lich / Dragon Priest
            (200,  380), // 4  High Daedra
            (160,  300), // 5  Vampire Lord / Soul Cairn
            (140,  260), // 6  Elite Construct
            (100,  200), // 7  Falmer / Mid Daedra / Giant
            ( 80,  180), // 8  Vampire / Gargoyle / Werewolf
            ( 60,  140), // 9  Draugr / Atronach / Skeleton Lord
            ( 45,  110), // 10 Spriggan / Hagrave / Troll
            ( 30,   80), // 11 Dangerous Wildlife
            ( 15,  120), // 12 Humanoid Enemy
            ( 10,   60), // 13 Standard Creature
            (  1,   20), // 14 Vermin / Passive Animal
        };

        private static readonly string[] Names = new[]
        {
            "Cosmic",
            "World-ender",
            "Dragon",
            "Ancient Lich / Dragon Priest",
            "High Daedra",
            "Vampire Lord / Soul Cairn",
            "Elite Construct",
            "Falmer / Mid Daedra / Giant",
            "Vampire / Gargoyle / Werewolf",
            "Draugr / Atronach / Skeleton Lord",
            "Spriggan / Hagrave / Troll",
            "Dangerous Wildlife",
            "Humanoid Enemy",
            "Standard Creature",
            "Vermin / Passive Animal",
        };

        public static (int Min, int Max) GetRange(int tier, int worldMaxLevel)
        {
            var (bMin, bMax) = BaseRanges[tier];
            double scale = worldMaxLevel / 1000.0;
            return ((int)Math.Round(bMin * scale), (int)Math.Round(bMax * scale));
        }

        public static string GetName(int tier) => Names[tier];
    }

    // -------------------------------------------------------------------------
    // Per-NPC data collected in the pre-pass
    // -------------------------------------------------------------------------
    public class NpcAssessment
    {
        public FormKey FormKey { get; set; }
        public string EditorId { get; set; } = "";
        public int Tier { get; set; } = 12;
        public int? EffectiveLevel { get; set; }
        public int CalcMax { get; set; }
        public float EquipmentScore { get; set; }
        // Peer group: faction EditorID (first hostile faction found), race FormKey
        public string? FactionKey { get; set; }
        public FormKey RaceFormKey { get; set; }
    }

    public class Program
    {
        private static Lazy<Settings> _lazySettings = null!;
        private static Settings Settings => _lazySettings.Value;
        private static readonly Random Rng = new Random(42);

        public static List<string> highPoweredNpcs = new();

        // Civilian class terms — NPCs with Unique flag and one of these class terms stay PC-scaled
        private static readonly List<string> CivilianClassTerms = new()
        {
            "smith", "alchem", "enchant", "vendor", "apothec",
            "innkeep", "merchant", "farmer", "miner", "beggar",
            "priest", "bard", "jarl", "steward"
        };

        // Classes whose NPCs are skipped during class rebuild (vendors, crafters, etc.)
        private static readonly List<string> ExcludedClasses = new()
            { "smith", "alchem", "enchant", "vendor", "apothec" };

        public static HashSet<IFormLinkGetter<INpcGetter>> npcsToIgnore = new()
        {
            Skyrim.Npc.MQ101Bear, Skyrim.Npc.WatchesTheRootsCorpse, Skyrim.Npc.BreyaCorpse,
            Skyrim.Npc.WatchesTheRoots, Skyrim.Npc.Drennen, Skyrim.Npc.Breya,
            Skyrim.Npc.dunHunterBear, Dawnguard.Npc.DLC1HowlSummonWerewolf,
        };

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings("Settings", "settings.json", out _lazySettings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "skyrim_releveled.esp")
                .Run(args);
        }

        // -------------------------------------------------------------------------
        // Level utilities
        // -------------------------------------------------------------------------
        public static int? GetEffectiveLevel(INpcGetter npc)
        {
            switch (npc.Configuration.Level)
            {
                case INpcLevelGetter fixedLevel when fixedLevel.Level > 0:
                    return fixedLevel.Level;
                case IPcLevelMultGetter:
                    if (npc.Configuration.CalcMaxLevel > 0) return npc.Configuration.CalcMaxLevel;
                    if (npc.Configuration.CalcMinLevel > 0) return npc.Configuration.CalcMinLevel;
                    return null;
                default:
                    return null;
            }
        }

        public static string GetStem(string editorId)
        {
            string s = editorId;
            foreach (var prefix in Settings.StripPrefixes.OrderByDescending(p => p.Length))
            {
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                { s = s.Substring(prefix.Length); break; }
            }
            s = s.TrimEnd('0','1','2','3','4','5','6','7','8','9');
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (var suffix in Settings.StripSuffixes.OrderByDescending(x => x.Length))
                {
                    if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        s = s.Substring(0, s.Length - suffix.Length)
                             .TrimEnd('0','1','2','3','4','5','6','7','8','9');
                        stripped = true; break;
                    }
                }
            }
            if (string.IsNullOrEmpty(s)) return editorId;
            foreach (var g in Settings.GenericStemWords)
                if (s.Equals(g, StringComparison.OrdinalIgnoreCase)) return editorId;
            foreach (var sw in Settings.ShortStemExceptions)
                if (s.StartsWith(sw, StringComparison.OrdinalIgnoreCase)) return sw.ToLowerInvariant();
            return s.ToLowerInvariant();
        }

        public static (int Min, int Max) GetTrimmedRange(List<int> levels, float cutoff)
        {
            if (levels.Count == 0) return (1, 1);
            var sorted = levels.OrderBy(x => x).ToList();
            int trimCount = (int)Math.Floor(sorted.Count * cutoff);
            int lo = Math.Min(trimCount, sorted.Count - 1);
            int hi = Math.Max(sorted.Count - 1 - trimCount, lo);
            return (sorted[lo], sorted[hi]);
        }

        public static decimal MapLevel(int value, int srcMin, int srcMax, int tgtMin, int tgtMax)
        {
            int span = Math.Max(srcMax - srcMin, 1);
            decimal t = (decimal)(value - srcMin) / span;
            return Math.Round(t * (tgtMax - tgtMin) + tgtMin);
        }

        public static float GetBonusPercent(string? editorId)
        {
            if (!Settings.EnableBonusKeywords || string.IsNullOrEmpty(editorId)) return 0f;
            float best = 0f;
            foreach (var kw in Settings.BonusKeywords)
                if (!string.IsNullOrEmpty(kw.Keyword)
                    && editorId.Contains(kw.Keyword, StringComparison.OrdinalIgnoreCase)
                    && kw.BonusPercent > best)
                    best = kw.BonusPercent;
            return best;
        }

        // -------------------------------------------------------------------------
        // Classifier — assigns a tier (0-14) to an NPC
        // -------------------------------------------------------------------------
        private static bool ContainsAny(string? s, params string[] patterns)
        {
            if (s is null) return false;
            foreach (var p in patterns)
                if (s.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static int ClassifyNpc(INpcGetter npc, ILinkCache lc)
        {
            string? editorId  = npc.EditorID;
            string? npcName   = npc.Name?.String;
            string? raceEditorId = null;

            if (npc.Race.TryResolve(lc, out var race))
                raceEditorId = race.EditorID;

            // --- Step 1: broad category from race keywords ---
            int tier = ClassifyByRaceKeyword(race, raceEditorId);

            // --- Step 2: NPC EditorID / display name overrides (applied after race, only upgrade) ---
            tier = ApplyNpcNameOverrides(tier, editorId, npcName);

            // --- Step 3: NPC keyword overrides (vampire flags) ---
            tier = ApplyNpcKeywordOverrides(npc, tier, lc);

            return tier;
        }

        private static int ClassifyByRaceKeyword(IRaceGetter? race, string? raceEditorId)
        {
            if (race is null) return 12; // default humanoid

            // Priority order matches requirements
            if (race.HasKeyword(Skyrim.Keyword.ActorTypeDragon))
                return ClassifyDragon(raceEditorId);

            if (race.HasKeyword(Skyrim.Keyword.ActorTypeDaedra))
                return ClassifyDaedra(raceEditorId);

            if (race.HasKeyword(Skyrim.Keyword.ActorTypeUndead))
                return ClassifyUndead(raceEditorId);

            if (race.HasKeyword(Skyrim.Keyword.ActorTypeDwarven))
                return ClassifyConstruct(raceEditorId);

            if (race.HasKeyword(Skyrim.Keyword.ActorTypeGhost))
                return 9; // spirits/ghosts classified as undead tier
            // Werewolf detection via race EditorID (no vanilla FormKey constant available)
            if (race.EditorID?.Contains("Werewolf", StringComparison.OrdinalIgnoreCase) == true ||
                race.EditorID?.Contains("Werebear",  StringComparison.OrdinalIgnoreCase) == true ||
                race.EditorID?.Contains("Werebat",   StringComparison.OrdinalIgnoreCase) == true)
                return 8;

            if (race.HasKeyword(Skyrim.Keyword.ActorTypeNPC))
                return ClassifyHumanoid(raceEditorId);

            if (race.HasKeyword(Skyrim.Keyword.ActorTypeAnimal))
                return 14;

            if (race.HasKeyword(Skyrim.Keyword.ActorTypeCreature))
                return ClassifyCreature(raceEditorId);

            // No keyword matched — fall back to humanoid
            return 12;
        }

        private static int ClassifyDragon(string? raceId)
        {
            if (raceId is null) return 2;
            if (raceId.Equals("AlduinRace", StringComparison.OrdinalIgnoreCase)) return 1;
            if (ContainsAny(raceId, "DragonPriest")) return 3;
            return 2;
        }

        private static int ClassifyDaedra(string? raceId)
        {
            if (raceId is null) return 4;
            // High daedra tier
            if (ContainsAny(raceId, "Dremora", "Xivilai", "Xivkyn", "GoldenSaint", "DarkSeducer"))
                return 4;
            // Mid daedra tier
            if (ContainsAny(raceId, "Scamp", "Clannfear", "Daedroth"))
                return 7;
            return 4; // default Daedra = high
        }

        private static int ClassifyUndead(string? raceId)
        {
            if (raceId is null) return 9;
            if (ContainsAny(raceId, "VampireLord", "VampireBeast", "SoulCairn")) return 5;
            if (ContainsAny(raceId, "Lich")) return 3;
            // Everything else: draugr, skeleton, spirit, wraith, etc.
            return 9;
        }

        private static int ClassifyConstruct(string? raceId)
        {
            if (raceId is null) return 9;
            if (ContainsAny(raceId, "Centurion", "Forgemaster", "Colossus", "Golem")) return 6;
            return 9;
        }

        private static int ClassifyHumanoid(string? raceId)
        {
            if (raceId is null) return 12;
            if (ContainsAny(raceId, "SnowElf", "Falmer")) return 7;
            return 12;
        }

        private static int ClassifyCreature(string? raceId)
        {
            if (raceId is null) return 13;
            if (ContainsAny(raceId, "Mammoth", "Giant", "Troll", "Hagrave", "Spriggan", "Chaurus", "Falmer"))
                return 7;
            if (ContainsAny(raceId, "Bear", "Sabrecat", "Wolf", "Spider", "DeathHound",
                                    "Gargoyle", "IceWraith", "Wispmother", "Wispmother", "Wisp"))
                return 11;
            return 13;
        }

        private static int ApplyNpcNameOverrides(int currentTier, string? editorId, string? name)
        {
            // Only upgrades (lower tier number = more powerful). Never downgrades.
            // Cosmic override
            if (ContainsAny(editorId, "MolagBal", "Jyggalag", "Shoggoth", "DaedricPrince") ||
                ContainsAny(name,     "MolagBal", "Jyggalag", "Shoggoth", "Daedric Prince"))
                return Math.Min(currentTier, 0);

            // Dragon priest / lich elevation
            if (ContainsAny(editorId, "DragonPriest", "Lich", "Necromancer") ||
                ContainsAny(name,     "Dragon Priest", "Lich", "Necromancer"))
                return Math.Min(currentTier, 3);

            // Vampire elevation
            if (ContainsAny(editorId, "VampireLord") || ContainsAny(name, "Vampire Lord"))
                return Math.Min(currentTier, 5);
            if (ContainsAny(editorId, "Vampire") || ContainsAny(name, "Vampire"))
                return Math.Min(currentTier, 8);

            // Undead elevation
            if (ContainsAny(editorId, "Zombie", "Undead", "Ghost", "Wraith", "Shade") ||
                ContainsAny(name,     "Zombie", "Undead", "Ghost", "Wraith", "Shade"))
                return Math.Min(currentTier, 9);

            // Construct elevation
            if (ContainsAny(editorId, "Golem", "Construct", "Automaton", "Centurion") ||
                ContainsAny(name,     "Golem", "Construct", "Automaton", "Centurion"))
                return Math.Min(currentTier, 6);

            return currentTier;
        }

        private static int ApplyNpcKeywordOverrides(INpcGetter npc, int currentTier, ILinkCache lc)
        {
            // DLC1_IS_Vampire — resolve by EditorID since the FormKey constant isn't always available
            if (npc.Keywords?.Any(k =>
                    k.TryResolve(lc, out var kw) &&
                    (kw.EditorID?.Equals("DLC1_IS_Vampire", StringComparison.OrdinalIgnoreCase) == true))
                == true)
                return Math.Min(currentTier, 5);

            // Generic Vampire keyword → at least Tier 8
            if (npc.HasKeyword(Skyrim.Keyword.Vampire))
                return Math.Min(currentTier, 8);

            return currentTier;
        }

        // -------------------------------------------------------------------------
        // Equipment score — reuses existing skill-weight population
        // -------------------------------------------------------------------------
        public static float ComputeEquipmentScore(INpcGetter npc, ILinkCache lc)
        {
            IDictionary<Skill, float> w = new Dictionary<Skill, float>();
            foreach (Skill sk in Enum.GetValues(typeof(Skill))) w[sk] = 0f;

            PopulateByInventory(npc, w, lc);
            PopulateBySpells(npc, w, lc);

            // Outfit population requires a mutable Npc — approximate via inventory only
            // (outfit weights already partially captured by inventory traversal)

            float total = w.Values.Sum();
            return total > 0 ? Math.Min(total / 20f, 1f) : 0f;
        }

        // -------------------------------------------------------------------------
        // Civilian detection
        // Two paths:
        //   A) Unique humanoid with civilian class term → always civilian
        //   B) Unique humanoid with a display name and NO qualifying hostile faction
        //      → named character (3DNPC, EZPG, base game named NPCs, etc.)
        // Both get PC-scaling with a low random cap.
        // factionMemberCount may be null during pre-pass A (first count sweep) — in
        // that case only path A applies.
        // -------------------------------------------------------------------------
        private static bool IsCivilian(INpcGetter npc, ILinkCache lc,
            Dictionary<string, int>? factionMemberCount = null,
            int minPeers = 3,
            Func<string, bool>? isNonHostile = null)
        {
            if (!npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique)) return false;
            if (!npc.Race.TryResolve(lc, out var race)) return false;
            if (!race.HasKeyword(Skyrim.Keyword.ActorTypeNPC)) return false;

            // Path A: civilian class term
            if (npc.Class.TryResolve(lc, out var cls))
            {
                string clsId   = cls.EditorID ?? "";
                string clsName = cls.Name?.String ?? "";
                if (CivilianClassTerms.Any(t =>
                    clsId.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    clsName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            // Path B: named NPC with no meaningful hostile faction
            if (factionMemberCount is not null && isNonHostile is not null)
            {
                // Must have a display name to be a "named character"
                if (string.IsNullOrWhiteSpace(npc.Name?.String)) return false;

                // If they belong to ANY qualifying hostile faction, they're a combatant
                foreach (var rankEntry in npc.Factions)
                {
                    if (!rankEntry.Faction.TryResolve(lc, out var fac) || fac.EditorID is null) continue;
                    if (isNonHostile(fac.EditorID)) continue;
                    if (factionMemberCount.TryGetValue(fac.EditorID, out var cnt) && cnt >= minPeers)
                        return false; // belongs to a real hostile faction → not civilian
                }
                return true; // unique, named, humanoid, no hostile faction → named character
            }

            return false;
        }

        // -------------------------------------------------------------------------
        // Skill redistribution
        // -------------------------------------------------------------------------
        private static void DistributeSkills(
            IReadOnlyDictionary<Skill, byte> skillWeights,
            IDictionary<Skill, byte> skillValues,
            int skillPoints)
        {
            float weightSum;
            bool firstPass = true;
            byte maxSkill = Settings.NPCMaxSkillLevel;
            var tempWeights = skillWeights.ToList();
            do
            {
                int overflow = 0;
                weightSum = tempWeights.Any() ? tempWeights.Sum(x => x.Value) : 0;
                for (int i = tempWeights.Count - 1; i >= 0; --i)
                {
                    if (firstPass) skillValues[tempWeights[i].Key] = 15;
                    skillValues[tempWeights[i].Key] += (byte)(skillPoints * (tempWeights[i].Value / weightSum));
                    if (skillValues[tempWeights[i].Key] > maxSkill)
                    {
                        overflow += skillValues[tempWeights[i].Key] - maxSkill;
                        skillValues[tempWeights[i].Key] = maxSkill;
                        tempWeights.RemoveAt(i);
                    }
                }
                firstPass = false;
                skillPoints = overflow;
            } while (skillPoints > 0 && weightSum > 0);
        }

        private static bool RelevelNPCSkills(Npc npc, ILinkCache linkCache)
        {
            if (Settings.NPCSkillsPerLevel <= 0) return false;
            if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Stats) && !npc.Template.IsNull)
                return false;
            if (npc.PlayerSkills is null || npc.Configuration.Level is not NpcLevel npcLevel) return false;
            if (!npc.Class.TryResolve(linkCache, out var classGetter)) return false;
            DistributeSkills(classGetter.SkillWeights, npc.PlayerSkills.SkillValues,
                (int)Math.Round(Settings.NPCSkillsPerLevel * npcLevel.Level));
            return true;
        }

        // -------------------------------------------------------------------------
        // Class rebuild
        // -------------------------------------------------------------------------
        private static void GetItemSkillWeights(IItemGetter item, IDictionary<Skill, float> w, float div = 1)
        {
            if (item is IWeaponGetter wpn && wpn.Data?.Skill is { } sk)
                w[(Skill)sk] += 1 / div;
            else if (item is IArmorGetter arm)
            {
                if (arm.HasKeyword(Skyrim.Keyword.ArmorShield)) w[Skill.Block] += 1 / div;
                if (arm.HasKeyword(Skyrim.Keyword.ArmorHeavy)) w[Skill.HeavyArmor] += 1 / div;
                else if (arm.HasKeyword(Skyrim.Keyword.ArmorLight)) w[Skill.LightArmor] += 1 / div;
            }
        }

        private static void GetLvliSkillWeights(ILeveledItemGetter lvli, IDictionary<Skill, float> w,
            ILinkCache lc, float div = 1)
        {
            var nodes = lvli.Entries?.ToList() ?? new();
            while (nodes.Any())
            {
                var n = nodes[^1]; nodes.RemoveAt(nodes.Count - 1);
                if (n.Data is null || !n.Data.Reference.TryResolve(lc, out var entry)) continue;
                if (entry is ILeveledItemGetter sub)
                    foreach (var c in sub.Entries.EmptyIfNull()) nodes.Add(c);
                else GetItemSkillWeights(entry, w, div);
            }
        }

        private static void GetSpellSkillWeights(ISpellRecordGetter spell, IDictionary<Skill, float> w,
            ILinkCache lc, float div = 1)
        {
            if (spell is not ISpellGetter sg || sg.Type != SpellType.Spell) return;
            foreach (var eff in sg.Effects)
            {
                if (!eff.BaseEffect.TryResolve(lc, out var mgef)) continue;
                var addTo = mgef.MagicSkill switch
                {
                    ActorValue.Destruction  => (Skill?)Skill.Destruction,
                    ActorValue.Alteration   => Skill.Alteration,
                    ActorValue.Conjuration  => Skill.Conjuration,
                    ActorValue.Illusion     => Skill.Illusion,
                    ActorValue.Restoration  => Skill.Restoration,
                    _ => null
                };
                if (addTo is { } s) { w[s] += 1 / div; return; }
            }
        }

        private static void GetLvlSpellWeights(ILeveledSpellGetter lvls, IDictionary<Skill, float> w,
            ILinkCache lc, float div = 1)
        {
            var nodes = lvls.Entries?.ToList() ?? new();
            while (nodes.Any())
            {
                var n = nodes[^1]; nodes.RemoveAt(nodes.Count - 1);
                if (n.Data is null || !n.Data.Reference.TryResolve(lc, out var entry)) continue;
                if (entry is ILeveledSpellGetter sub)
                    foreach (var c in sub.Entries.EmptyIfNull()) nodes.Add(c);
                else GetSpellSkillWeights(entry, w, lc, div);
            }
        }

        private static void PopulateByInventory(INpcSpawnGetter spawn, IDictionary<Skill, float> w, ILinkCache lc)
        {
            var nodes = new List<(INpcSpawnGetter, int)>();
            if (spawn is INpcGetter ng)
            {
                if (ng.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Inventory)
                    && ng.Template.TryResolve(lc, out var t)) nodes.Add((t, 1));
                else foreach (var e in ng.Items.EmptyIfNull())
                    if (e.Item.Item.TryResolve(lc, out var it))
                    { if (it is ILeveledItemGetter li) GetLvliSkillWeights(li, w, lc); else GetItemSkillWeights(it, w); }
            }
            else if (spawn is ILeveledNpcGetter ln)
                foreach (var e in ln.Entries.EmptyIfNull())
                    if (e.Data?.Reference.TryResolve(lc, out var s) == true) nodes.Add((s, ln.Entries!.Count));

            while (nodes.Any())
            {
                var (node, div) = nodes[^1]; nodes.RemoveAt(nodes.Count - 1);
                if (node is ILeveledNpcGetter l2)
                    foreach (var c in l2.Entries.EmptyIfNull())
                        if (c.Data?.Reference.TryResolve(lc, out var cs) == true) nodes.Add((cs, l2.Entries!.Count));
                else if (node is INpcGetter sg2)
                {
                    if (sg2.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Inventory)
                        && sg2.Template.TryResolve(lc, out var t2)) nodes.Add((t2, 1));
                    else foreach (var e in sg2.Items.EmptyIfNull())
                        if (e.Item.Item.TryResolve(lc, out var it))
                        { if (it is ILeveledItemGetter li) GetLvliSkillWeights(li, w, lc, div); else GetItemSkillWeights(it, w, div); }
                }
            }
        }

        private static void PopulateBySpells(INpcSpawnGetter spawn, IDictionary<Skill, float> w, ILinkCache lc)
        {
            var nodes = new List<(INpcSpawnGetter, int)>();
            if (spawn is INpcGetter ng)
            {
                if (ng.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Inventory)
                    && ng.Template.TryResolve(lc, out var t)) nodes.Add((t, 1));
                else foreach (var sp in ng.ActorEffect.EmptyIfNull())
                    if (sp.TryResolve(lc, out var sr))
                    { if (sr is ILeveledSpellGetter ls) GetLvlSpellWeights(ls, w, lc); else GetSpellSkillWeights(sr, w, lc); }
            }
            else if (spawn is ILeveledNpcGetter ln)
                foreach (var e in ln.Entries.EmptyIfNull())
                    if (e.Data?.Reference.TryResolve(lc, out var s) == true) nodes.Add((s, ln.Entries!.Count));

            while (nodes.Any())
            {
                var (node, div) = nodes[^1]; nodes.RemoveAt(nodes.Count - 1);
                if (node is ILeveledNpcGetter l2)
                    foreach (var c in l2.Entries.EmptyIfNull())
                        if (c.Data?.Reference.TryResolve(lc, out var cs) == true) nodes.Add((cs, l2.Entries!.Count));
                else if (node is INpcGetter sg2)
                {
                    if (sg2.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Inventory)
                        && sg2.Template.TryResolve(lc, out var t2)) nodes.Add((t2, 1));
                    else foreach (var sp in sg2.ActorEffect.EmptyIfNull())
                        if (sp.TryResolve(lc, out var sr))
                        { if (sr is ILeveledSpellGetter ls) GetLvlSpellWeights(ls, w, lc, div); else GetSpellSkillWeights(sr, w, lc, div); }
                }
            }
        }

        private static void PopulateByOutfit(Npc npc, IDictionary<Skill, float> w, ILinkCache lc)
        {
            if (npc.DefaultOutfit.IsNull || !npc.DefaultOutfit.TryResolve(lc, out var outfit)) return;
            foreach (var entry in outfit!.Items.EmptyIfNull())
                if (entry.TryResolve(lc, out var eg))
                {
                    if (eg is ILeveledItemGetter li) GetLvliSkillWeights(li, w, lc);
                    else if (eg is IArmorGetter arm)
                    {
                        if (arm.HasKeyword(Skyrim.Keyword.ArmorShield)) w[Skill.Block] += 1;
                        if (arm.HasKeyword(Skyrim.Keyword.ArmorHeavy)) w[Skill.HeavyArmor] += 1;
                        else if (arm.HasKeyword(Skyrim.Keyword.ArmorLight)) w[Skill.LightArmor] += 1;
                    }
                }
        }

        private static void CalculateClassWeights(Class cls, IDictionary<Skill, float> w)
        {
            var list = w.Where(e => e.Value > 0).ToList();
            list.Sort((x, y) => x.Value >= y.Value ? 1 : -1);
            float last = list[0].Value;
            for (int rank = 1, i = 0; i < list.Count; i++)
            {
                if (list[i].Value > last) { rank = i + 1; last = list[i].Value; }
                list[i] = new(list[i].Key, rank);
            }
            bool isHybrid =
                list.Any(x => x.Key is Skill.Block or Skill.OneHanded or Skill.TwoHanded or Skill.LightArmor or Skill.HeavyArmor) &&
                list.Any(x => x.Key is Skill.Illusion or Skill.Alteration or Skill.Conjuration or Skill.Destruction or Skill.Restoration);
            if (isHybrid)
            {
                float total = cls.StatWeights.Sum(x => x.Value);
                float combatSum = cls.StatWeights[BasicStat.Health] > cls.StatWeights[BasicStat.Magicka]
                    ? cls.StatWeights[BasicStat.Health] + cls.StatWeights[BasicStat.Stamina]
                    : cls.StatWeights[BasicStat.Magicka] + cls.StatWeights[BasicStat.Stamina];
                float magicSum = cls.StatWeights[BasicStat.Magicka] > cls.StatWeights[BasicStat.Health]
                    ? cls.StatWeights[BasicStat.Magicka] + cls.StatWeights[BasicStat.Stamina]
                    : cls.StatWeights[BasicStat.Health] + cls.StatWeights[BasicStat.Stamina];
                if (cls.StatWeights[BasicStat.Health] == cls.StatWeights[BasicStat.Magicka])
                    total -= cls.StatWeights[BasicStat.Stamina];
                float cr = combatSum / total, mr = magicSum / total;
                for (int i = 0; i < list.Count; i++)
                    list[i] = list[i].Key switch
                    {
                        Skill.Block or Skill.OneHanded or Skill.TwoHanded or Skill.LightArmor or Skill.HeavyArmor
                            => new(list[i].Key, (float)Math.Round(list[i].Value * cr)),
                        Skill.Illusion or Skill.Alteration or Skill.Conjuration or Skill.Destruction or Skill.Restoration
                            => new(list[i].Key, (float)Math.Round(list[i].Value * mr)),
                        _ => list[i]
                    };
            }
            list.ForEach(x => w[x.Key] = x.Value);
            if (!isHybrid && w[Skill.Block] == 0 && (w[Skill.OneHanded] > 0 || w[Skill.TwoHanded] > 0))
                w[Skill.Block] = 1;
        }

        private static bool RebalanceClassValues(Npc npc, IPatcherState<ISkyrimMod, ISkyrimModGetter> state, ILinkCache lc)
        {
            if (!Settings.RebuildNPCClasses) return false;
            if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Stats) && !npc.Template.IsNull) return false;
            if (!npc.Class.TryResolve(lc, out var cls)) return false;
            if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique)
                && ExcludedClasses.Any(e => (cls.EditorID?.Contains(e, StringComparison.OrdinalIgnoreCase) ?? false)
                                         || (cls.Name?.String?.Contains(e, StringComparison.OrdinalIgnoreCase) ?? false)))
                return false;

            IDictionary<Skill, float> w = new Dictionary<Skill, float>();
            cls.SkillWeights.ForEach(x => w[x.Key] = 0);
            PopulateByInventory(npc, w, lc);
            PopulateBySpells(npc, w, lc);
            PopulateByOutfit(npc, w, lc);
            w.ForEach(x => w[x.Key] = (float)Math.Ceiling(x.Value));
            if (w.All(x => x.Value == 0)) return false;

            var newClass = state.PatchMod.Classes.AddNew();
            newClass.DeepCopyIn(cls);
            newClass.EditorID = "SRClass" + npc.EditorID;
            npc.Class = newClass.ToLink();
            CalculateClassWeights(newClass, w);
            newClass.SkillWeights.ForEach(x => newClass.SkillWeights[x.Key] = 0);
            w.ForEach(x => newClass.SkillWeights[x.Key] = (byte)x.Value);
            return true;
        }

        // -------------------------------------------------------------------------
        // Perk distribution
        // -------------------------------------------------------------------------
        private static bool GetTreeFromSkill(Skill s, ILinkCache lc, out IActorValueInformationGetter? av)
        {
            av = s switch
            {
                Skill.Alchemy     => Skyrim.ActorValueInformation.AVAlchemy.TryResolve(lc),
                Skill.Alteration  => Skyrim.ActorValueInformation.AVAlteration.TryResolve(lc),
                Skill.Archery     => Skyrim.ActorValueInformation.AVMarksman.TryResolve(lc),
                Skill.Block       => Skyrim.ActorValueInformation.AVBlock.TryResolve(lc),
                Skill.Conjuration => Skyrim.ActorValueInformation.AVConjuration.TryResolve(lc),
                Skill.Destruction => Skyrim.ActorValueInformation.AVDestruction.TryResolve(lc),
                Skill.Enchanting  => Skyrim.ActorValueInformation.AVEnchanting.TryResolve(lc),
                Skill.HeavyArmor  => Skyrim.ActorValueInformation.AVHeavyArmor.TryResolve(lc),
                Skill.Illusion    => Skyrim.ActorValueInformation.AVMysticism.TryResolve(lc),
                Skill.LightArmor  => Skyrim.ActorValueInformation.AVLightArmor.TryResolve(lc),
                Skill.Lockpicking => Skyrim.ActorValueInformation.AVLockpicking.TryResolve(lc),
                Skill.OneHanded   => Skyrim.ActorValueInformation.AVOneHanded.TryResolve(lc),
                Skill.Pickpocket  => Skyrim.ActorValueInformation.AVPickpocket.TryResolve(lc),
                Skill.Restoration => Skyrim.ActorValueInformation.AVRestoration.TryResolve(lc),
                Skill.Smithing    => Skyrim.ActorValueInformation.AVSmithing.TryResolve(lc),
                Skill.Sneak       => Skyrim.ActorValueInformation.AVSneak.TryResolve(lc),
                Skill.Speech      => Skyrim.ActorValueInformation.AVSpeechcraft.TryResolve(lc),
                Skill.TwoHanded   => Skyrim.ActorValueInformation.AVTwoHanded.TryResolve(lc),
                _ => null
            };
            return av is not null;
        }

        private static bool PerformCompare<T>(IConditionGetter? c, T l, T r) where T : IComparable<T>
            => c?.CompareOperator switch
            {
                CompareOperator.EqualTo              => l.CompareTo(r) == 0,
                CompareOperator.GreaterThan          => l.CompareTo(r) > 0,
                CompareOperator.GreaterThanOrEqualTo => l.CompareTo(r) >= 0,
                CompareOperator.LessThan             => l.CompareTo(r) < 0,
                CompareOperator.LessThanOrEqualTo    => l.CompareTo(r) <= 0,
                CompareOperator.NotEqualTo           => l.CompareTo(r) != 0,
                _ => false
            };

        private static void RemoveVanillaPerks(Npc npc, ILinkCache vanillaCache)
        {
            if (npc.Perks is null || npc.Perks.Count == 0) return;
            for (int i = npc.Perks.Count - 1; i >= 0; --i)
            {
                if (npc.Perks[i].Perk.Equals(Skyrim.Perk.AlchemySkillBoosts) ||
                    npc.Perks[i].Perk.Equals(Skyrim.Perk.PerkSkillBoosts)) continue;
                if (vanillaCache.TryResolve<IPerkGetter>(npc.Perks[i].Perk, out _))
                    npc.Perks.RemoveAt(i);
            }
        }

        private static bool FulfillsPerkConditions(Npc npc, IPerkGetter perk, Skill skill, ILinkCache lc)
        {
            if (npc.Perks!.Any(x => x.Perk.Equals(perk.ToLink()))) return false;
            foreach (var cond in perk.Conditions)
            {
                if (cond is not ConditionFloat cf) continue;
                if (cf.Data is GetBaseActorValueConditionData avd && (int)avd.ActorValue == (int)skill)
                { if (!PerformCompare(cf, npc.PlayerSkills!.SkillValues[skill], cf.ComparisonValue)) return false; }
                else if (cf.Data is HasPerkConditionData pd && pd.Perk.Link.TryResolve(lc, out var req))
                {
                    bool has = npc.Perks!.Any(x => x.Perk.Equals(req.ToLink()));
                    if ((cf.CompareOperator == CompareOperator.EqualTo    && cf.ComparisonValue == 1 && !has) ||
                        (cf.CompareOperator == CompareOperator.NotEqualTo && cf.ComparisonValue == 1 && has)  ||
                        (cf.CompareOperator == CompareOperator.EqualTo    && cf.ComparisonValue == 0 && has)  ||
                        (cf.CompareOperator == CompareOperator.NotEqualTo && cf.ComparisonValue == 0 && !has))
                        return false;
                }
                else return false;
            }
            return true;
        }

        private static bool DistributeNPCPerks(Npc npc, ILinkCache lc, ILinkCache vanillaCache,
            ExcludedList excludedPerks)
        {
            if (Settings.NPCPerksPerLevel <= 0) return false;
            if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.SpellList) && !npc.Template.IsNull) return false;
            if (npc.PlayerSkills is null) return false;
            if (!npc.Class.TryResolve<IClassGetter>(lc, out var cls)) return false;
            if (!npc.Race.TryResolve<IRaceGetter>(lc, out var race)) return false;
            if (!race.HasKeyword(Skyrim.Keyword.ActorTypeNPC) && !race.HasKeyword(Skyrim.Keyword.ActorTypeUndead)) return false;
            foreach (var kw in Settings.PerkDistributionFilter)
                if (npc.HasKeyword(kw) || race.HasKeyword(kw)) return false;

            float perksTotal = Settings.NPCPerksPerLevel;
            if (npc.Configuration.Level is NpcLevel nl) perksTotal *= nl.Level;

            npc.Perks ??= new();
            if (Settings.RemoveVanillaPerks) RemoveVanillaPerks(npc, vanillaCache);

            int overflow = 0;
            var perkDist = cls.SkillWeights.ToList();
            float wSum = perkDist.Any() ? perkDist.Sum(x => x.Value) : 0;

            foreach (var pw in perkDist)
            {
                if (pw.Value <= 0) continue;
                byte toSpend = (byte)Math.Round(overflow + perksTotal * (pw.Value / wSum));
                if (toSpend <= 0) continue;
                if (!GetTreeFromSkill(pw.Key, lc, out var tree) || tree!.PerkTree is null) continue;

                while (toSpend > 0)
                {
                    bool added = false;
                    foreach (var node in tree.PerkTree)
                    {
                        if (toSpend <= 0) break;
                        if (!node.Perk.TryResolve(lc, out var perkEntry) || perkEntry.EditorID is null) continue;
                        if (excludedPerks.Keys.Any(k => perkEntry.EditorID.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                            !excludedPerks.ForbiddenKeys.Any(k => perkEntry.EditorID.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                        if (FulfillsPerkConditions(npc, perkEntry, pw.Key, lc))
                        {
                            --toSpend; added = true;
                            npc.Perks!.Add(new PerkPlacement { Perk = perkEntry.ToLink(), Rank = 1 });
                        }
                        while (toSpend > 0 && perkEntry.NextPerk.TryResolve<IPerkGetter>(lc, out perkEntry))
                        {
                            if (FulfillsPerkConditions(npc, perkEntry, pw.Key, lc))
                            { --toSpend; added = true; npc.Perks!.Add(new PerkPlacement { Perk = perkEntry.ToLink(), Rank = 1 }); }
                            else break;
                        }
                    }
                    if (!added) { overflow = toSpend; break; }
                    else overflow = 0;
                }
            }
            if (npc.Perks.Count == 0) npc.Perks = null;
            return true;
        }

        private static void DisableExtraDamagePerks(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (!Settings.DisableExtraDamagePerks) return;
            foreach (var perkGetter in state.LoadOrder.PriorityOrder.Perk().WinningOverrides()
                .Where(x => x.EditorID?.Contains("crExtraDamage", StringComparison.OrdinalIgnoreCase) == true))
            {
                var perk = state.PatchMod.Perks.GetOrAddAsOverride(perkGetter);
                perk.Effects.Clear();
            }
        }

        public static void ApplyLevel(INpc npc, short newLevel)
        {
            npc.Configuration.Level = new NpcLevel() { Level = newLevel };
            npc.Configuration.CalcMinLevel = 1;
            npc.Configuration.CalcMaxLevel = newLevel;
        }

        public static void PrintWarnings()
        {
            if (highPoweredNpcs.Count > 0)
            {
                Console.WriteLine($"Warning: {highPoweredNpcs.Count} NPCs assigned level > 100:");
                foreach (var item in highPoweredNpcs) Console.WriteLine("  " + item);
            }
        }

        // -------------------------------------------------------------------------
        // RunPatch
        // -------------------------------------------------------------------------
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (state.ExtraSettingsDataPath is null)
            { Console.Error.WriteLine("ERROR: ExtraSettingsDataPath is null."); return; }

            // Load named NPC overrides
            var namedPath = Path.Combine(state.ExtraSettingsDataPath, "named_npcs.json");
            if (!File.Exists(namedPath)) { Console.Error.WriteLine($"ERROR: Missing {namedPath}"); return; }
            var namedNpcs = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(namedPath),
                new JsonSerializerSettings { Error = (s, a) => a.ErrorContext.Handled = true }) ?? new();
            Console.WriteLine("=== Skyrim Releveler — Loading Data ===");
            Console.WriteLine($"  Named NPC overrides  : {namedNpcs.Count}");

            // Load custom followers
            var followersPath = Path.Combine(state.ExtraSettingsDataPath, "customFollowers.json");
            var customFollowers = File.Exists(followersPath)
                ? JsonConvert.DeserializeObject<FollowerList>(File.ReadAllText(followersPath)) ?? new()
                : new FollowerList();

            // Load excluded NPCs
            var exclNpcsPath = Path.Combine(state.ExtraSettingsDataPath, "excludedNPCs.json");
            var excludedNPCs = File.Exists(exclNpcsPath)
                ? JsonConvert.DeserializeObject<ExcludedList>(File.ReadAllText(exclNpcsPath)) ?? new()
                : new ExcludedList();

            // Load excluded perks
            var exclPerksPath = Path.Combine(state.ExtraSettingsDataPath, "excludedPerks.json");
            var excludedPerks = File.Exists(exclPerksPath)
                ? JsonConvert.DeserializeObject<ExcludedList>(File.ReadAllText(exclPerksPath)) ?? new()
                : new ExcludedList();

            // Load race level modifiers
            var racePath = Path.Combine(state.ExtraSettingsDataPath, "raceLevelModifiers.json");
            var raceModifiers = File.Exists(racePath)
                ? JsonConvert.DeserializeObject<RaceModifierList>(File.ReadAllText(racePath)) ?? new()
                : new RaceModifierList();

            ILinkCache? vanillaCache = null;
            ILinkCache GetVanillaCache() => vanillaCache ??= LoadOrder.Import<ISkyrimModGetter>(
                state.DataFolderPath,
                new List<ModKey> { Skyrim.ModKey, Dawnguard.ModKey, Dragonborn.ModKey },
                GameRelease.SkyrimSE).ToImmutableLinkCache();

            Console.WriteLine("  Data files loaded. Starting pre-pass...");
            Console.WriteLine();

            var linkCache = state.LoadOrder.PriorityOrder.ToImmutableLinkCache();

            // -----------------------------------------------------------------------
            // Helper closures
            // -----------------------------------------------------------------------
            static List<string> SplitTokens(string s)
            {
                var tokens = new List<string>(); int start = 0;
                for (int i = 1; i <= s.Length; i++)
                {
                    bool b = i == s.Length
                        || (char.IsUpper(s[i]) && i > 0 && char.IsLower(s[i-1]))
                        || (char.IsUpper(s[i]) && i+1 < s.Length && char.IsLower(s[i+1]) && char.IsUpper(s[i-1]))
                        || (char.IsDigit(s[i]) != char.IsDigit(s[i-1]));
                    if (b) { var t = s.Substring(start, i - start); if (t.Length > 0) tokens.Add(t); start = i; }
                }
                return tokens;
            }

            int? FindNamedLevel(string? editorId)
            {
                if (string.IsNullOrEmpty(editorId)) return null;
                var et = SplitTokens(editorId);
                foreach (var (key, level) in namedNpcs)
                {
                    var kt = SplitTokens(key);
                    if (kt.Count == 0) continue;
                    for (int i = 0; i <= et.Count - kt.Count; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < kt.Count; j++)
                            if (!string.Equals(et[i+j], kt[j], StringComparison.OrdinalIgnoreCase)) { match = false; break; }
                        if (match) return level;
                    }
                }
                return null;
            }

            bool IsExcluded(string editorId) =>
                excludedNPCs.Keys.Any(k => editorId.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                !excludedNPCs.ForbiddenKeys.Any(k => editorId.Contains(k, StringComparison.OrdinalIgnoreCase));

            bool IsFollower(INpcGetter npc)
            {
                if (!Settings.ScaleFollowers || npc.EditorID is null) return false;
                if (npc.Factions.Any(f => f.Faction.Equals(Skyrim.Faction.PotentialFollowerFaction) ||
                                          f.Faction.Equals(Skyrim.Faction.PotentialHireling))) return true;
                foreach (var entry in customFollowers.Followers)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    if (!npc.EditorID.Contains(entry.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.ForbiddenKeys.Any(fk => npc.EditorID.Contains(fk, StringComparison.OrdinalIgnoreCase))) continue;
                    return true;
                }
                return false;
            }

            void GetRaceModifier(INpcGetter npc, out short add, out float mult)
            {
                add = 0; mult = 1f;
                if (!npc.Race.TryResolve(linkCache, out var race) || race.EditorID is null) return;
                foreach (var entry in raceModifiers.Data)
                    if (entry.Keys.Any(k => race.EditorID.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                        !entry.ForbiddenKeys.Any(k => race.EditorID.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    { add = entry.LevelModifierAdd; mult = entry.LevelModifierMult; return; }
            }

            // -----------------------------------------------------------------------
            // Pre-pass A: first sweep — collect faction membership counts and
            // per-NPC data so we can pick the LARGEST hostile faction per NPC.
            // -----------------------------------------------------------------------

            // Skip-condition helper — used in both pre-pass sweeps.
            // During pre-pass A (first sweep) factionMemberCount is not yet built,
            // so IsCivilian only uses Path A (class terms).
            // During pre-pass B and the main pass we pass the full faction data.
            bool ShouldSkip(INpcGetter g, string edId,
                Dictionary<string, int>? facCounts = null)
            {
                if (npcsToIgnore.Contains(g)) return true;
                if (g.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsCharGenFacePreset)) return true;
                if (g.HasKeyword(Skyrim.Keyword.PlayerKeyword)) return true;
                if (IsExcluded(edId)) return true;
                if (IsFollower(g)) return true;
                if (FindNamedLevel(edId) is not null) return true;
                if (IsCivilian(g, linkCache, facCounts, minPeers: Settings.AutoFactionMinPeers,
                    isNonHostile: IsNonHostileFaction)) return true;
                return false;
            }

            // factionMemberCount[factionEditorId] = total NPC count across load order
            var factionMemberCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Non-hostile faction EditorID substrings to skip during faction selection
            static bool IsNonHostileFaction(string facId) =>
                facId.Contains("Follower",   StringComparison.OrdinalIgnoreCase) ||
                facId.Contains("Vendor",     StringComparison.OrdinalIgnoreCase) ||
                facId.Contains("Player",     StringComparison.OrdinalIgnoreCase) ||
                facId.Contains("Crime",      StringComparison.OrdinalIgnoreCase) ||
                facId.Contains("Merchant",   StringComparison.OrdinalIgnoreCase) ||
                facId.Contains("Hireling",   StringComparison.OrdinalIgnoreCase) ||
                facId.Contains("Potential",  StringComparison.OrdinalIgnoreCase) ||
                facId.Contains("Steward",    StringComparison.OrdinalIgnoreCase);

            foreach (var getter in state.LoadOrder.PriorityOrder.Npc().WinningOverrides())
            {
                if (getter.EditorID is not { } edId0) continue;
                if (ShouldSkip(getter, edId0)) continue;  // pre-pass A: no faction data yet
                foreach (var rankEntry in getter.Factions)
                {
                    if (!rankEntry.Faction.TryResolve(linkCache, out var fac) || fac.EditorID is null) continue;
                    if (IsNonHostileFaction(fac.EditorID)) continue;
                    if (!factionMemberCount.TryGetValue(fac.EditorID, out var cnt))
                        factionMemberCount[fac.EditorID] = 1;
                    else
                        factionMemberCount[fac.EditorID] = cnt + 1;
                }
            }

            // -----------------------------------------------------------------------
            // Pre-pass B: full assessment — classify each NPC, pick largest faction
            // -----------------------------------------------------------------------
            // assessments[formKey] = NpcAssessment
            var assessments = new Dictionary<FormKey, NpcAssessment>();
            // factionEditorId -> effective levels of all members (for source range)
            var factionEffLevels = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            // race peers: raceFormKey -> effective levels
            var raceEffLevels = new Dictionary<FormKey, List<int>>();
            // tier members for no-faction fallback
            var tierEffLevels = new Dictionary<int, List<int>>();

            foreach (var getter in state.LoadOrder.PriorityOrder.Npc().WinningOverrides())
            {
                if (getter.EditorID is not { } edId) continue;
                if (ShouldSkip(getter, edId, factionMemberCount)) continue;  // pre-pass B: full data

                int? effLevel = GetEffectiveLevel(getter);
                int tier = ClassifyNpc(getter, linkCache);

                // Pick the LARGEST hostile faction this NPC belongs to
                string? bestFaction = null;
                int bestCount = 0;
                foreach (var rankEntry in getter.Factions)
                {
                    if (!rankEntry.Faction.TryResolve(linkCache, out var fac) || fac.EditorID is null) continue;
                    if (IsNonHostileFaction(fac.EditorID)) continue;
                    int cnt = factionMemberCount.TryGetValue(fac.EditorID, out var c) ? c : 0;
                    if (cnt > bestCount) { bestCount = cnt; bestFaction = fac.EditorID; }
                }

                FormKey raceFormKey = getter.Race.FormKey;
                float equipScore = ComputeEquipmentScore(getter, linkCache);
                int calcMax = getter.Configuration.CalcMaxLevel;

                var assessment = new NpcAssessment
                {
                    FormKey        = getter.FormKey,
                    EditorId       = edId,
                    Tier           = tier,
                    EffectiveLevel = effLevel,
                    CalcMax        = calcMax,
                    EquipmentScore = equipScore,
                    FactionKey     = bestFaction,
                    RaceFormKey    = raceFormKey,
                };
                assessments[getter.FormKey] = assessment;

                // Accumulate effective levels into peer lists
                if (effLevel.HasValue && effLevel.Value > 0)
                {
                    if (bestFaction is not null)
                    {
                        if (!factionEffLevels.TryGetValue(bestFaction, out var fl))
                            factionEffLevels[bestFaction] = fl = new List<int>();
                        fl.Add(effLevel.Value);
                    }

                    if (!raceEffLevels.TryGetValue(raceFormKey, out var rl))
                        raceEffLevels[raceFormKey] = rl = new List<int>();
                    rl.Add(effLevel.Value);

                    if (!tierEffLevels.TryGetValue(tier, out var tl))
                        tierEffLevels[tier] = tl = new List<int>();
                    tl.Add(effLevel.Value);
                }
            }

            // Sort all level lists for trimmed-range calculation
            foreach (var lst in factionEffLevels.Values) lst.Sort();
            foreach (var lst in raceEffLevels.Values)    lst.Sort();
            foreach (var lst in tierEffLevels.Values)    lst.Sort();

            float cutoff = Settings.OutlierPercentileCutoff;
            int   minPeers = Settings.AutoFactionMinPeers;

            // Pre-compute trimmed source ranges for each faction
            var factionSourceRange = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fk, lvls) in factionEffLevels)
                if (lvls.Count >= minPeers)
                    factionSourceRange[fk] = GetTrimmedRange(lvls, cutoff);

            var raceSourceRange = new Dictionary<FormKey, (int Min, int Max)>();
            foreach (var (rk, lvls) in raceEffLevels)
                if (lvls.Count >= 1)
                    raceSourceRange[rk] = GetTrimmedRange(lvls, cutoff);

            // Stem groups as Path 3 — within same tier only, minimum 5 members
            // stemKey = tier.ToString() + "|" + stem
            var stemEffLevels = new Dictionary<string, List<int>>();
            foreach (var a in assessments.Values)
            {
                if (a.EffectiveLevel is null || a.EffectiveLevel.Value <= 0) continue;
                // Only use stem path for NPCs that have no qualifying faction
                if (a.FactionKey is not null && factionSourceRange.ContainsKey(a.FactionKey)) continue;
                string stem = GetStem(a.EditorId);
                string stemKey = $"{a.Tier}|{stem}";
                if (!stemEffLevels.TryGetValue(stemKey, out var sl))
                    stemEffLevels[stemKey] = sl = new List<int>();
                sl.Add(a.EffectiveLevel.Value);
            }
            foreach (var lst in stemEffLevels.Values) lst.Sort();

            var stemSourceRange = new Dictionary<string, (int Min, int Max)>();
            foreach (var (sk, lvls) in stemEffLevels)
                if (lvls.Count >= 5)
                    stemSourceRange[sk] = GetTrimmedRange(lvls, cutoff);

            var tierSourceRange = new Dictionary<int, (int Min, int Max)>();
            foreach (var (tk, lvls) in tierEffLevels)
                tierSourceRange[tk] = GetTrimmedRange(lvls, cutoff);

            Console.WriteLine();
            Console.WriteLine("=== Skyrim Releveler — Assessment Pass ===");
            Console.WriteLine($"  NPCs assessed        : {assessments.Count}");
            Console.WriteLine($"  Factions with ranges : {factionSourceRange.Count}");
            Console.WriteLine($"  Stem groups (Path 3) : {stemSourceRange.Count}");
            Console.WriteLine($"  Tiers covered        : {tierSourceRange.Count}");
            Console.WriteLine();

            // -----------------------------------------------------------------------
            // Level computation — faction-first, then race, then tier-global
            // -----------------------------------------------------------------------
            short ComputeLevel(NpcAssessment a)
            {
                int tier = a.Tier;
                var (tMin, tMax) = TierSystem.GetRange(tier, Settings.WorldMaxLevel);
                int effLvl = a.EffectiveLevel ?? 0;

                decimal baseLevel;
                string mode;

                // PATH 1: largest hostile faction with enough members
                if (a.FactionKey is not null && factionSourceRange.TryGetValue(a.FactionKey, out var fsr))
                {
                    int clamped = Math.Clamp(effLvl > 0 ? effLvl : fsr.Min, fsr.Min, fsr.Max);
                    baseLevel = effLvl > 0
                        ? MapLevel(clamped, fsr.Min, fsr.Max, tMin, tMax)
                        : Math.Round((tMin + tMax) / 2m);
                    mode = $"faction({a.FactionKey}) src=[{fsr.Min},{fsr.Max}]";
                }
                // PATH 2: race peers
                else if (raceSourceRange.TryGetValue(a.RaceFormKey, out var rsr))
                {
                    int clamped = Math.Clamp(effLvl > 0 ? effLvl : rsr.Min, rsr.Min, rsr.Max);
                    baseLevel = effLvl > 0
                        ? MapLevel(clamped, rsr.Min, rsr.Max, tMin, tMax)
                        : Math.Round((tMin + tMax) / 2m);
                    mode = $"race src=[{rsr.Min},{rsr.Max}]";
                }
                // PATH 3: EditorID stem group (within same tier, ≥5 members)
                else
                {
                    string stem = GetStem(a.EditorId);
                    string stemKey = $"{tier}|{stem}";
                    if (stemSourceRange.TryGetValue(stemKey, out var ssr))
                    {
                        int clamped = Math.Clamp(effLvl > 0 ? effLvl : ssr.Min, ssr.Min, ssr.Max);
                        baseLevel = effLvl > 0
                            ? MapLevel(clamped, ssr.Min, ssr.Max, tMin, tMax)
                            : Math.Round((tMin + tMax) / 2m);
                        mode = $"stem({stem}) src=[{ssr.Min},{ssr.Max}]";
                    }
                    // PATH 4: equipment score within tier range (pure fallback)
                    else
                    {
                        baseLevel = Math.Round(tMin + (decimal)a.EquipmentScore * (tMax - tMin));
                        mode = "equipment-score";
                    }
                }

                if (Settings.PrintDebugOutput)
                    Console.WriteLine($"  {a.EditorId}: tier {tier} ({TierSystem.GetName(tier)}) [{tMin},{tMax}] via {mode} -> base {baseLevel}");

                return (short)Math.Clamp(baseLevel, tMin, tMax);
            }

            // -----------------------------------------------------------------------
            // Main pass: assign levels and apply all subsystems
            // -----------------------------------------------------------------------
            int followersScaled = 0, namedCount = 0, civiliansScaled = 0,
                pipelineCount = 0, npcsProcessed = 0;

            foreach (var getter in state.LoadOrder.PriorityOrder.Npc().WinningOverrides())
            {
                if (npcsToIgnore.Contains(getter)) continue;
                if (getter.EditorID is not { } editorId) continue;
                if (getter.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsCharGenFacePreset) ||
                    getter.HasKeyword(Skyrim.Keyword.PlayerKeyword)) continue;
                if (IsExcluded(editorId)) continue;

                Npc npcCopy = getter.DeepCopy();
                bool wasChanged = false;

                // Priority 1: followers
                if (IsFollower(getter))
                {
                    npcCopy.Configuration.Level = new PcLevelMult { LevelMult = 1f };
                    npcCopy.Configuration.CalcMinLevel = Math.Max(npcCopy.Configuration.CalcMinLevel, (short)1);
                    npcCopy.Configuration.CalcMaxLevel = short.MaxValue;
                    ++followersScaled;
                    wasChanged = true;
                    if (Settings.PrintDebugOutput) Console.WriteLine($"  {editorId}: follower -> unlimited scaling");
                }
                // Priority 2: named overrides
                else if (FindNamedLevel(editorId) is { } namedLevel)
                {
                    GetRaceModifier(getter, out var rAdd, out var rMult);
                    short fixedLevel = (short)Math.Clamp(
                        Math.Round(namedLevel * rMult) + rAdd + Settings.GlobalOffset, 1, short.MaxValue);
                    if (Settings.PrintDebugOutput) Console.WriteLine($"  {editorId}: named -> {fixedLevel}");
                    ApplyLevel(npcCopy, fixedLevel);
                    wasChanged = true;
                    ++namedCount;
                }
                // Priority 3: civilians (class-based) and named characters (no hostile faction)
                else if (IsCivilian(getter, linkCache, factionMemberCount,
                    Settings.AutoFactionMinPeers, IsNonHostileFaction))
                {
                    npcCopy.Configuration.Level = new PcLevelMult { LevelMult = 1f };
                    npcCopy.Configuration.CalcMinLevel = 1;
                    short civMax = (short)Rng.Next(10, 51);
                    npcCopy.Configuration.CalcMaxLevel = civMax;
                    ++civiliansScaled;
                    wasChanged = true;
                    if (Settings.PrintDebugOutput) Console.WriteLine($"  {editorId}: civilian -> PC-scaled cap {civMax}");
                }
                // Priority 4: assessment pipeline
                else if (assessments.TryGetValue(getter.FormKey, out var assessment))
                {
                    short baseLevel = ComputeLevel(assessment);

                    // Post-score adjustments
                    decimal adjusted = baseLevel + Settings.GlobalOffset;
                    GetRaceModifier(getter, out var rAdd, out var rMult);
                    adjusted = Math.Round(adjusted * (decimal)rMult) + rAdd;

                    float bonusPct = GetBonusPercent(editorId);
                    if (bonusPct > 0f)
                        adjusted = Math.Round(adjusted * (decimal)(1f + bonusPct / 100f));

                    short newLevel = (short)Math.Clamp(adjusted, 1, short.MaxValue);
                    if (newLevel > 100) highPoweredNpcs.Add(editorId);

                    if (Settings.PrintDebugOutput)
                        Console.WriteLine($"    -> final {newLevel} (offset={Settings.GlobalOffset}, raceMult={rMult}, raceAdd={rAdd}, bonus={bonusPct}%)");

                    ApplyLevel(npcCopy, newLevel);
                    wasChanged = true;
                    ++pipelineCount;
                }

                // All NPCs: class rebuild, skill redistribution, perk distribution
                wasChanged |= RebalanceClassValues(npcCopy, state, linkCache);
                wasChanged |= RelevelNPCSkills(npcCopy, linkCache);
                wasChanged |= DistributeNPCPerks(npcCopy, linkCache, GetVanillaCache(), excludedPerks);

                if (wasChanged)
                    state.PatchMod.Npcs.Set(npcCopy);

                ++npcsProcessed;
                if (npcsProcessed % 2000 == 0)
                    Console.WriteLine($"  ... {npcsProcessed} NPCs processed");
            }

            DisableExtraDamagePerks(state);

            Console.WriteLine();
            Console.WriteLine("=== Skyrim Releveler Complete ===");
            Console.WriteLine($"  Total NPCs processed : {npcsProcessed}");
            Console.WriteLine($"  Releveled (pipeline) : {pipelineCount}");
            Console.WriteLine($"  Named overrides      : {namedCount}");
            Console.WriteLine($"  Civilians / named chars : {civiliansScaled}");
            Console.WriteLine($"  Followers            : {followersScaled}");
            Console.WriteLine();
            PrintWarnings();
        }
    }
}
