using System;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SkyrimReleveler
{
    /// <summary>
    /// Applies global multipliers to weapon damage, armor/shield ratings, and ammo damage.
    /// Logic: finalValue = Round(vanillaValue * multiplier), clamped to [0, 60000].
    /// Any multiplier of exactly 1.0 is a no-op — that category is skipped entirely.
    ///
    /// Uses WinningContextOverrides throughout so every record is fully resolved —
    /// partial overrides (e.g. USSEP touching only a script flag) never cause a stat
    /// to be silently skipped. GetOrAddAsOverride on the context writes cleanly to the
    /// patch mod without duplicating work.
    /// </summary>
    public static class StatScalerPatcher
    {
        public static void Run(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
        {
            bool patchWeapons = Math.Abs(settings.WeaponDamageMultiplier      - 1.0f) > 0.0001f;
            bool patchHeavy   = Math.Abs(settings.HeavyArmorRatingMultiplier  - 1.0f) > 0.0001f;
            bool patchLight   = Math.Abs(settings.LightArmorRatingMultiplier  - 1.0f) > 0.0001f;
            bool patchShield  = Math.Abs(settings.ShieldArmorRatingMultiplier - 1.0f) > 0.0001f;
            bool patchAmmo    = Math.Abs(settings.AmmoDamageMultiplier        - 1.0f) > 0.0001f;

            if (!patchWeapons && !patchHeavy && !patchLight && !patchShield && !patchAmmo)
            {
                Console.WriteLine("  StatScaler: all multipliers are 1.0 — skipping weapon/armor patching.");
                return;
            }

            // -----------------------------------------------------------------------
            // Weapons — scale BasicStats.Damage
            // -----------------------------------------------------------------------
            if (patchWeapons)
            {
                float mult = settings.WeaponDamageMultiplier;
                Console.WriteLine($"  StatScaler: applying weapon damage multiplier ×{mult:F4}");

                int patched = 0;
                foreach (var ctx in state.LoadOrder.PriorityOrder.Weapon().WinningContextOverrides())
                {
                    var weapon = ctx.Record;

                    if (weapon.BasicStats == null)
                        continue;

                    ushort originalDamage = weapon.BasicStats.Damage;

                    // 0-damage: staves, torches, placeholders
                    if (originalDamage == 0)
                        continue;

                    ushort newDamage = ClampUshort((int)Math.Round(originalDamage * mult));
                    if (newDamage == originalDamage)
                        continue;

                    var weaponOverride = ctx.GetOrAddAsOverride(state.PatchMod);
                    weaponOverride.BasicStats!.Damage = newDamage;
                    patched++;
                }

                Console.WriteLine($"  StatScaler: {patched} weapons patched.");
            }

            // -----------------------------------------------------------------------
            // Armor and Shields — scale ArmorRating
            // Shields: ArmorShield keyword. Heavy/light: ArmorHeavy / ArmorLight.
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

                foreach (var ctx in state.LoadOrder.PriorityOrder.Armor().WinningContextOverrides())
                {
                    var armor = ctx.Record;

                    if (armor.Keywords == null)
                        continue;

                    float originalRating = armor.ArmorRating;
                    if (originalRating <= 0f)
                        continue;

                    // Determine type — shield takes priority over heavy/light
                    bool isShield = false;
                    bool isHeavy  = false;
                    bool isLight  = false;

                    foreach (var kwLink in armor.Keywords)
                    {
                        if      (kwLink.Equals(Skyrim.Keyword.ArmorShield)) { isShield = true; break; }
                        else if (kwLink.Equals(Skyrim.Keyword.ArmorHeavy))  { isHeavy  = true; }
                        else if (kwLink.Equals(Skyrim.Keyword.ArmorLight))  { isLight  = true; }
                    }

                    float mult;
                    bool  shouldPatch;

                    if      (isShield) { mult = shieldMult; shouldPatch = patchShield; }
                    else if (isHeavy)  { mult = heavyMult;  shouldPatch = patchHeavy;  }
                    else if (isLight)  { mult = lightMult;  shouldPatch = patchLight;  }
                    else continue; // clothing or untyped — skip

                    if (!shouldPatch)
                        continue;

                    float newRating = ClampFloat((float)Math.Round(originalRating * mult));
                    if (Math.Abs(newRating - originalRating) < 0.01f)
                        continue;

                    var armorOverride = ctx.GetOrAddAsOverride(state.PatchMod);
                    armorOverride.ArmorRating = newRating;

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
            // -----------------------------------------------------------------------
            if (patchAmmo)
            {
                float mult = settings.AmmoDamageMultiplier;
                Console.WriteLine($"  StatScaler: applying ammo damage multiplier ×{mult:F4}");

                int patched = 0;
                foreach (var ctx in state.LoadOrder.PriorityOrder.Ammunition().WinningContextOverrides())
                {
                    var ammo = ctx.Record;

                    if (ammo.Flags.HasFlag(Ammunition.Flag.NonPlayable))
                        continue;

                    float originalDamage = ammo.Damage;
                    if (originalDamage <= 0f)
                        continue;

                    float newDamage = ClampFloat((float)Math.Round(originalDamage * mult, 2));
                    if (Math.Abs(newDamage - originalDamage) < 0.001f)
                        continue;

                    var ammoOverride = ctx.GetOrAddAsOverride(state.PatchMod);
                    ammoOverride.Damage = newDamage;
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
