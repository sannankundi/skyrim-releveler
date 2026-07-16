using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SkyrimReleveler
{
    public record ScoreResult(
        float Score,
        float Confidence,
        IReadOnlyList<string> FiredSignals);

    public static class ImportanceScorer
    {
        // Each entry: (signal name, delegate, weight)
        private static List<(string Name, Func<INpcGetter, ILinkCache, ImportanceWeights, float> Fn, float Weight)>
            _signals = new();

        /// <summary>
        /// Clears and rebuilds the active signal list from the provided weights.
        /// Signals whose weight == 0.0 are skipped (excluded from numerator and denominator).
        /// </summary>
        public static void Initialize(ImportanceWeights weights)
        {
            _signals = new List<(string, Func<INpcGetter, ILinkCache, ImportanceWeights, float>, float)>();

            Add("UniqueFlag",       UniqueFlag,       weights.UniqueFlag);
            Add("EssentialFlag",    EssentialFlag,    weights.EssentialFlag);
            Add("ProtectedFlag",    ProtectedFlag,    weights.ProtectedFlag);
            Add("NoRespawnFlag",    NoRespawnFlag,    weights.NoRespawnFlag);
            Add("CalcMinLevelHigh", CalcMinLevelHigh, weights.CalcMinLevelHigh);
            Add("CalcMaxLevelHigh", CalcMaxLevelHigh, weights.CalcMaxLevelHigh);
            Add("PcLevelMult",      PcLevelMult,      weights.PcLevelMult);
            Add("HighFactionRank",  HighFactionRank,  weights.HighFactionRank);
            Add("ManyFactions",     ManyFactions,     weights.ManyFactions);
            Add("ManyKeywords",     ManyKeywords,     weights.ManyKeywords);
            Add("HasCombatStyle",   HasCombatStyle,   weights.HasCombatStyle);
            Add("HasScripts",       HasScripts,       weights.HasScripts);
            Add("BossToken",        BossToken,        weights.BossToken);
            Add("UniqueVoiceType",  UniqueVoiceType,  weights.UniqueVoiceType);
            Add("ModOriginUnique",  ModOriginUnique,  weights.ModOriginUnique);
            Add("BossClass",        BossClass,        weights.BossClass);
            Add("ActorTypeKeyword", ActorTypeKeyword, weights.ActorTypeKeyword);

            void Add(string name,
                     Func<INpcGetter, ILinkCache, ImportanceWeights, float> fn,
                     float weight)
            {
                if (weight != 0.0f)
                    _signals.Add((name, fn, weight));
            }
        }

        /// <summary>
        /// Evaluates all active signals against <paramref name="npc"/> and returns a
        /// normalised ScoreResult. Guards against an empty signal list (returns Score 0).
        /// </summary>
        public static ScoreResult Score(INpcGetter npc, ILinkCache linkCache, ImportanceWeights weights, NpcAssessment? assessment = null)
        {
            // Resolve class once — used for child suppression and boss elevation
            string? classId = null;
            string? className = null;
            var resolvedClass = npc.Class.TryResolve(linkCache);
            if (resolvedClass is not null)
            {
                classId   = resolvedClass.EditorID ?? "";
                className = resolvedClass.Name?.String ?? "";
            }

            // Check if NPC has a boss-tier class — if so, civilian/child suppression is ignored
            bool hasBossClass = IsBossClass(classId, className, weights);

            // If child class and no boss class override → return zero score immediately
            if (!hasBossClass && IsChildClass(classId, className, weights))
                return new ScoreResult(0f, 1f, Array.Empty<string>());

            float numerator   = 0f;
            float denominator = 0f;
            var   firedNames  = new List<string>();

            foreach (var (name, fn, weight) in _signals)
            {
                float raw;
                try
                {
                    raw = fn(npc, linkCache, weights);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [WARNING] ImportanceScorer: signal '{name}' threw for NPC '{npc.EditorID}': {ex.Message}");
                    raw = 0f;
                }

                numerator   += raw * weight;
                denominator += weight;

                if (raw > 0f)
                    firedNames.Add(name);
            }

            float score = denominator > 0f
                ? Math.Clamp(numerator / denominator, 0f, 1f)
                : 0f;

            // --- Assessment-based signals (inline, need pre-computed peer data) ---
            if (assessment is not null)
            {
                void AddAssessment(string name, float raw, float weight)
                {
                    if (weight == 0f) return;
                    numerator   += raw * weight;
                    denominator += weight;
                    if (raw > 0f) firedNames.Add(name);
                }

                // HighSkillValue: any vanilla skill ≥ threshold
                float skillRaw = assessment.MaxSkillValue >= weights.HighSkillThreshold ? 1f : 0f;
                AddAssessment("HighSkillValue", skillRaw, weights.HighSkillValue);

                // HighVanillaPerks: vanilla perk count ≥ threshold
                float perkRaw = assessment.VanillaPerkCount >= weights.HighPerkCountThreshold ? 1f : 0f;
                AddAssessment("HighVanillaPerks", perkRaw, weights.HighVanillaPerks);

                // AbsoluteEquipment: equipment score ≥ threshold
                float absEquip = assessment.EquipmentScore >= weights.AbsoluteEquipThreshold ? 1f : 0f;
                AddAssessment("AbsoluteEquip", absEquip, weights.AbsoluteEquipment);

                // RelativeEquipment: NPC's equipment score ≥ ratio × peer average
                if (assessment.PeerAvgEquipScore > 0f)
                {
                    float ratio = assessment.EquipmentScore / assessment.PeerAvgEquipScore;
                    float relEquip = ratio >= weights.RelativeEquipRatio ? Math.Clamp((ratio - 1f) / 2f, 0f, 1f) : 0f;
                    AddAssessment("RelativeEquip", relEquip, weights.RelativeEquipment);
                }

                // UniqueItem: check if NPC carries a unique item (EditorID in UniqueItemKeywords or UESP known uniques)
                float uniqueItem = HasUniqueItem(npc, linkCache, weights) ? 1f : 0f;
                AddAssessment("UniqueItem", uniqueItem, weights.UniqueItem);
            }

            // Re-normalize after inline additions
            score = denominator > 0f
                ? Math.Clamp(numerator / denominator, 0f, 1f)
                : 0f;

            // --- Confidence via contradiction pairs ---
            float contradictionNumerator   = 0f;
            float contradictionDenominator = 0f;

            foreach (var pair in weights.ContradictionPairs)
            {
                bool aFired = firedNames.Contains(pair.SignalA);
                bool bFired = firedNames.Contains(pair.SignalB);
                if (aFired && bFired)
                    contradictionNumerator += pair.Weight;
                contradictionDenominator += pair.Weight;
            }

            float contradictionScore = contradictionDenominator > 0f
                ? Math.Clamp(contradictionNumerator / contradictionDenominator, 0f, 1f)
                : 0f;

            float confidence = 1f - contradictionScore;

            return new ScoreResult(score, confidence, firedNames.AsReadOnly());
        }

        private static bool IsBossClass(string? classId, string? className, ImportanceWeights w)
        {
            if (classId is null) return false;
            foreach (var token in w.BossClassTokens)
                if (classId.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    (className?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false))
                    return true;
            return false;
        }

        private static bool IsChildClass(string? classId, string? className, ImportanceWeights w)
        {
            if (classId is null) return false;
            foreach (var token in w.ChildClassTokens)
                if (classId.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                    (className?.Equals(token, StringComparison.OrdinalIgnoreCase) ?? false))
                    return true;
            return false;
        }

        /// <summary>
        /// Derives the floor level for an NPC within its tier range from a score in [0,1].
        /// Algorithm B from the design document.
        /// </summary>
        public static int DeriveFloor(float score, int tier, int worldMaxLevel)
        {
            var (tMin, tMax) = TierSystem.GetRange(tier, worldMaxLevel);
            int floorLevel   = tMin + (int)Math.Round(score * (tMax - tMin));
            return Math.Clamp(floorLevel, tMin, tMax);
        }

        // =====================================================================
        // Signal implementations (Task 3)
        // =====================================================================

        private static float UniqueFlag(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique) ? 1f : 0f;

        private static float EssentialFlag(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Essential) ? 1f : 0f;

        private static float ProtectedFlag(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Protected) ? 1f : 0f;

        private static float NoRespawnFlag(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Respawn) ? 1f : 0f;

        private static float CalcMinLevelHigh(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => npc.Configuration.CalcMinLevel >= w.CalcMinLevelThreshold ? 1f : 0f;

        private static float CalcMaxLevelHigh(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => npc.Configuration.CalcMaxLevel >= w.CalcMaxLevelThreshold ? 1f : 0f;

        private static float PcLevelMult(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => npc.Configuration.Level is IPcLevelMultGetter ? 1f : 0f;

        private static float HighFactionRank(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            foreach (var rankEntry in npc.Factions)
                if (rankEntry.Rank >= w.FactionRankThreshold)
                    return 1f;
            return 0f;
        }

        private static float ManyFactions(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => npc.Factions.Count >= w.MinFactionCount ? 1f : 0f;

        private static float ManyKeywords(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => (npc.Keywords?.Count ?? 0) >= w.MinKeywordCount ? 1f : 0f;

        private static float HasCombatStyle(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            var cs = npc.CombatStyle.TryResolve(lc);
            if (cs is null) return 0f;
            string? csId = cs.EditorID;
            if (csId is null) return 1f;
            return csId.Contains("unarmed", StringComparison.OrdinalIgnoreCase) ? 0f : 1f;
        }

        private static float HasScripts(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
            => (npc.VirtualMachineAdapter?.Scripts?.Count ?? 0) >= 1 ? 1f : 0f;

        private static float BossToken(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            string? eid = npc.EditorID;
            if (eid is null) return 0f;
            foreach (var token in w.BossTokens)
                if (eid.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return 1f;
            return 0f;
        }

        private static float UniqueVoiceType(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            var vt = npc.Voice.TryResolve(lc);
            if (vt is null) return 0f;
            string? vtId = vt.EditorID;
            if (vtId is null) return 0f;
            // Generic voice type → not unique
            foreach (var generic in w.GenericVoiceTypes)
                if (vtId.Equals(generic, StringComparison.OrdinalIgnoreCase))
                    return 0f;
            return 1f;
        }

        private static float ModOriginUnique(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            // Must have Unique flag
            if (!npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique)) return 0f;

            // Must NOT be from a vanilla ESM
            var modKey = npc.FormKey.ModKey;
            if (modKey == Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.ModKey)    return 0f;
            if (modKey == Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.ModKey) return 0f;
            if (modKey == Mutagen.Bethesda.FormKeys.SkyrimSE.Dragonborn.ModKey) return 0f;

            return 1f;
        }

        private static float BossClass(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            var cls = npc.Class.TryResolve(lc);
            if (cls is null) return 0f;
            return IsBossClass(cls.EditorID, cls.Name?.String, w) ? 1f : 0f;
        }

        private static float ActorTypeKeyword(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            if (npc.Keywords is null) return 0f;
            foreach (var kw in npc.Keywords)
            {
                var kwRecord = kw.TryResolve(lc);
                if (kwRecord?.EditorID is null) continue;
                foreach (var token in w.BossActorTypeKeywords)
                    if (kwRecord.EditorID.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return 1f;
            }
            return 0f;
        }

        private static bool HasUniqueItem(INpcGetter npc, ILinkCache lc, ImportanceWeights w)
        {
            var items = npc.Items;
            if (items is not null)
                foreach (var entry in items)
                {
                    var item = entry.Item.Item.TryResolve(lc);
                    if (item?.EditorID is not { } eid) continue;
                    foreach (var kw in w.UniqueItemKeywords)
                        if (eid.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            return true;
                }

            var outfit = npc.DefaultOutfit.TryResolve(lc);
            if (outfit?.Items is not null)
                foreach (var entry in outfit.Items)
                {
                    var outfitItem = entry.TryResolve(lc);
                    if (outfitItem?.EditorID is not { } oeid) continue;
                    foreach (var kw in w.UniqueItemKeywords)
                        if (oeid.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            return true;
                }

            return false;
        }
    }
}
