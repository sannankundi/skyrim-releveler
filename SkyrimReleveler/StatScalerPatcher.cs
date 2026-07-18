using System;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SkyrimReleveler
{
    /// <summary>
    /// Applies global multipliers to weapon damage and armor ratings.
    /// Logic: finalValue = Round(vanillaValue * multiplier), clamped to valid range.
    /// A multiplier of 1.0 is a no-op — nothing is patched.
    /// </summary>
    public static class StatScalerPatcher
    {
        /// <summary>
        /// Entry point called from RunPatch after NPC processing is complete.
        /// </summary>
        public static void Run(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
        {
            bool patchWeapons = Math.Abs(settings.WeaponDamageMultiplier      - 1.0f) > 0.0001f;
            bool patchHeavy   = Math.Abs(settings.HeavyArmorRatingMultiplier  - 1.0f) > 0.0001f;
            bool patchLight   = Math.Abs(settings.LightArmorRatingMultiplier  - 1.0f) > 0.0001f;

            if (!patchWeapons && !patchHeavy && !patchLight)
            {
                Console.WriteLine("  StatScaler: all multipliers are 1.0 — skipping weapon/armor patching.");
                return;
            }

            int weaponsPatched = 0;
            int heavyPatched   = 0;
            int lightPatched   = 0;

            // -----------------------------------------------------------------------
            // Weapons — scale BasicStats.Damage
            // -----------------------------------------------------------------------
            if (patchWeapons)
            {
                float mult = settings.WeaponDamageMultiplier;
                Console.WriteLine($"  StatScaler: applying weapon damage multiplier ×{mult:F4}");

                foreach (var weaponGetter in state.LoadOrder.PriorityOrder.Weapon().WinningOverrides())
                {
                    // Skip non-playable weapons — they have no meaningful damage to scale
                    if (weaponGetter.Data?.Flags.HasFlag(WeaponData.Flag.NonPlayable) == true)
                        continue;

                    // Skip template weapons — they inherit stats from their template
                    if (weaponGetter.Template?.FormKey is { } tKey && !tKey.IsNull)
                        continue;

                    if (weaponGetter.BasicStats == null)
                        continue;

                    ushort originalDamage = weaponGetter.BasicStats.Damage;
                    if (originalDamage == 0)
                        continue;

                    ushort newDamage = ClampUshort((int)Math.Round(originalDamage * mult));
                    if (newDamage == originalDamage)
                        continue;

                    // DeepCopy + Set is the correct Mutagen pattern for this version
                    var weaponOverride = weaponGetter.DeepCopy();
                    weaponOverride.BasicStats!.Damage = newDamage;
                    state.PatchMod.Weapons.Set(weaponOverride);
                    weaponsPatched++;
                }

                Console.WriteLine($"  StatScaler: {weaponsPatched} weapons patched.");
            }

            // -----------------------------------------------------------------------
            // Armor — scale ArmorRating, split by Heavy/Light keyword
            // -----------------------------------------------------------------------
            if (patchHeavy || patchLight)
            {
                float heavyMult = settings.HeavyArmorRatingMultiplier;
                float lightMult = settings.LightArmorRatingMultiplier;

                if (patchHeavy) Console.WriteLine($"  StatScaler: applying heavy armor rating multiplier ×{heavyMult:F4}");
                if (patchLight) Console.WriteLine($"  StatScaler: applying light armor rating multiplier ×{lightMult:F4}");

                var linkCache = state.LoadOrder.PriorityOrder.ToImmutableLinkCache();

                foreach (var armorGetter in state.LoadOrder.PriorityOrder.Armor().WinningOverrides())
                {
                    // Resolve keywords to determine armor type
                    bool isHeavy = false;
                    bool isLight = false;

                    if (armorGetter.Keywords != null)
                    {
                        foreach (var kwLink in armorGetter.Keywords)
                        {
                            if (kwLink.Equals(Skyrim.Keyword.ArmorHeavy)) { isHeavy = true; break; }
                            if (kwLink.Equals(Skyrim.Keyword.ArmorLight)) { isLight = true; break; }
                        }
                    }

                    if (!isHeavy && !isLight)
                        continue;

                    float mult       = isHeavy ? heavyMult : lightMult;
                    bool shouldPatch = isHeavy ? patchHeavy : patchLight;

                    if (!shouldPatch)
                        continue;

                    float originalRating = armorGetter.ArmorRating;
                    if (originalRating <= 0f)
                        continue;

                    float newRating = ClampFloat((float)Math.Round(originalRating * mult));
                    if (Math.Abs(newRating - originalRating) < 0.01f)
                        continue;

                    var armorOverride = armorGetter.DeepCopy();
                    armorOverride.ArmorRating = newRating;
                    state.PatchMod.Armors.Set(armorOverride);

                    if (isHeavy) heavyPatched++;
                    else         lightPatched++;
                }

                if (patchHeavy) Console.WriteLine($"  StatScaler: {heavyPatched} heavy armor pieces patched.");
                if (patchLight) Console.WriteLine($"  StatScaler: {lightPatched} light armor pieces patched.");
            }
        }

        private static ushort ClampUshort(int value) =>
            (ushort)Math.Max(0, Math.Min(60000, value));

        private static float ClampFloat(float value) =>
            Math.Max(0f, Math.Min(60000f, value));
    }
}
