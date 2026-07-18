using System;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SkyrimReleveler
{
    /// <summary>
    /// Applies global multipliers to weapon damage and armor/shield ratings.
    /// Logic: finalValue = Round(vanillaValue * multiplier), clamped to [0, 60000].
    /// Any multiplier of exactly 1.0 is a no-op — that category is skipped entirely.
    /// </summary>
    public static class StatScalerPatcher
    {
        public static void Run(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
        {
            bool patchWeapons = Math.Abs(settings.WeaponDamageMultiplier       - 1.0f) > 0.0001f;
            bool patchHeavy   = Math.Abs(settings.HeavyArmorRatingMultiplier   - 1.0f) > 0.0001f;
            bool patchLight   = Math.Abs(settings.LightArmorRatingMultiplier   - 1.0f) > 0.0001f;
            bool patchShield  = Math.Abs(settings.ShieldArmorRatingMultiplier  - 1.0f) > 0.0001f;
            bool patchAmmo    = Math.Abs(settings.AmmoDamageMultiplier         - 1.0f) > 0.0001f;

            if (!patchWeapons && !patchHeavy && !patchLight && !patchShield && !patchAmmo)
            {
                Console.WriteLine("  StatScaler: all multipliers are 1.0 — skipping weapon/armor patching.");
                return;
            }

            // -----------------------------------------------------------------------
            // Weapons — scale BasicStats.Damage
            // Uses WinningContextOverrides so we can resolve the full record via
            // the link cache when a partial override doesn't include BasicStats.
            // -----------------------------------------------------------------------
            if (patchWeapons)
            {
                float mult = settings.WeaponDamageMultiplier;
                Console.WriteLine($"  StatScaler: applying weapon damage multiplier ×{mult:F4}");

                var linkCache = state.LoadOrder.PriorityOrder.ToImmutableLinkCache();
                int patched = 0;

                foreach (var weaponGetter in state.LoadOrder.PriorityOrder.Weapon().WinningOverrides())
                {
                    // If the winning override has BasicStats, use it directly.
                    // If not (partial override), resolve the full record via link cache.
                    ushort originalDamage;
                    if (weaponGetter.BasicStats != null)
                    {
                        originalDamage = weaponGetter.BasicStats.Damage;
                    }
                    else if (linkCache.TryResolve<IWeaponGetter>(weaponGetter.FormKey, out var resolved)
                             && resolved.BasicStats != null)
                    {
                        originalDamage = resolved.BasicStats.Damage;
                    }
                    else
                    {
                        continue;
                    }

                    // 0-damage records are staves, torches, or placeholders — skip them
                    if (originalDamage == 0)
                        continue;

                    ushort newDamage = ClampUshort((int)Math.Round(originalDamage * mult));
                    if (newDamage == originalDamage)
                        continue;

                    var weaponOverride = weaponGetter.DeepCopy();
                    // Ensure BasicStats exists on the override before writing
                    weaponOverride.BasicStats ??= new WeaponBasicStats();
                    weaponOverride.BasicStats.Damage = newDamage;
                    state.PatchMod.Weapons.Set(weaponOverride);
                    patched++;
                }

                Console.WriteLine($"  StatScaler: {patched} weapons patched.");
            }

            // -----------------------------------------------------------------------
            // Armor and Shields — scale ArmorRating
            // Shields use the ArmorShield keyword and are Armor records in Skyrim's data.
            // Heavy/Light armor is identified by ArmorHeavy / ArmorLight keywords.
            // A single record can only be one type; shields are checked first.
            // -----------------------------------------------------------------------
            if (patchHeavy || patchLight || patchShield)
            {
                float heavyMult  = settings.HeavyArmorRatingMultiplier;
                float lightMult  = settings.LightArmorRatingMultiplier;
                float shieldMult = settings.ShieldArmorRatingMultiplier;

                if (patchHeavy)  Console.WriteLine($"  StatScaler: applying heavy armor rating multiplier ×{heavyMult:F4}");
                if (patchLight)  Console.WriteLine($"  StatScaler: applying light armor rating multiplier ×{lightMult:F4}");
                if (patchShield) Console.WriteLine($"  StatScaler: applying shield armor rating multiplier ×{shieldMult:F4}");

                int heavyPatched  = 0;
                int lightPatched  = 0;
                int shieldPatched = 0;

                foreach (var armorGetter in state.LoadOrder.PriorityOrder.Armor().WinningOverrides())
                {
                    if (armorGetter.Keywords == null)
                        continue;

                    float originalRating = armorGetter.ArmorRating;
                    if (originalRating <= 0f)
                        continue;

                    // Determine type — shield takes priority over heavy/light
                    bool isShield = false;
                    bool isHeavy  = false;
                    bool isLight  = false;

                    foreach (var kwLink in armorGetter.Keywords)
                    {
                        if      (kwLink.Equals(Skyrim.Keyword.ArmorShield)) { isShield = true; break; }
                        else if (kwLink.Equals(Skyrim.Keyword.ArmorHeavy))  { isHeavy  = true; }
                        else if (kwLink.Equals(Skyrim.Keyword.ArmorLight))  { isLight  = true; }
                    }

                    float mult;
                    bool  shouldPatch;

                    if (isShield)       { mult = shieldMult; shouldPatch = patchShield; }
                    else if (isHeavy)   { mult = heavyMult;  shouldPatch = patchHeavy;  }
                    else if (isLight)   { mult = lightMult;  shouldPatch = patchLight;  }
                    else continue; // clothing or untyped — skip

                    if (!shouldPatch)
                        continue;

                    float newRating = ClampFloat((float)Math.Round(originalRating * mult));
                    if (Math.Abs(newRating - originalRating) < 0.01f)
                        continue;

                    var armorOverride = armorGetter.DeepCopy();
                    armorOverride.ArmorRating = newRating;
                    state.PatchMod.Armors.Set(armorOverride);

                    if      (isShield) shieldPatched++;
                    else if (isHeavy)  heavyPatched++;
                    else               lightPatched++;
                }

                if (patchHeavy)  Console.WriteLine($"  StatScaler: {heavyPatched}  heavy armor pieces patched.");
                if (patchLight)  Console.WriteLine($"  StatScaler: {lightPatched}  light armor pieces patched.");
                if (patchShield) Console.WriteLine($"  StatScaler: {shieldPatched} shields patched.");
            }

            // -----------------------------------------------------------------------
            // Ammo — scale Damage (arrows, bolts, etc.)
            // Flags and Damage are direct properties on IAmmunitionGetter.
            // Non-playable records (internal engine placeholders) are skipped.
            // -----------------------------------------------------------------------
            if (patchAmmo)
            {
                float mult = settings.AmmoDamageMultiplier;
                Console.WriteLine($"  StatScaler: applying ammo damage multiplier ×{mult:F4}");

                int patched = 0;
                foreach (var ammoGetter in state.LoadOrder.PriorityOrder.Ammunition().WinningOverrides())
                {
                    // Skip non-playable ammo (engine placeholders, etc.)
                    if (ammoGetter.Flags.HasFlag(Ammunition.Flag.NonPlayable))
                        continue;

                    float originalDamage = ammoGetter.Damage;
                    if (originalDamage <= 0f)
                        continue;

                    float newDamage = ClampFloat((float)Math.Round(originalDamage * mult, 2));
                    if (Math.Abs(newDamage - originalDamage) < 0.001f)
                        continue;

                    var ammoOverride = ammoGetter.DeepCopy();
                    ammoOverride.Damage = newDamage;
                    state.PatchMod.Ammunitions.Set(ammoOverride);
                    patched++;
                }

                Console.WriteLine($"  StatScaler: {patched} ammo records patched.");
            }
        }

        private static ushort ClampUshort(int value) =>
            (ushort)Math.Max(0, Math.Min(60000, value));

        private static float ClampFloat(float value) =>
            Math.Max(0f, Math.Min(60000f, value));
    }
}
