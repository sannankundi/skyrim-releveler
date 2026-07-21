using System;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Noggog;

namespace SkyrimReleveler
{
    /// <summary>
    /// Applies global multipliers to weapon damage, armor/shield ratings, ammo damage,
    /// and per-tier unarmed/melee damage multipliers to race records.
    ///
    /// NOTE: Skyrim's SpellDamageMult and AttackDamageMult race fields are stored in a
    /// custom binary block (Flags2/DATA) that Mutagen does not expose as named properties.
    /// The only directly writable combat multiplier on a Race record via Mutagen is
    /// UnarmedDamage (base unarmed damage value). For spell and general attack scaling,
    /// use the crNerfDamage perk system or Game Setting tweaks instead.
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
                    if (weapon.BasicStats == null) continue;

                    ushort originalDamage = weapon.BasicStats.Damage;
                    if (originalDamage == 0) continue;

                    ushort newDamage = ClampUshort((int)Math.Round(originalDamage * mult));
                    if (newDamage == originalDamage) continue;

                    var weaponOverride = ctx.GetOrAddAsOverride(state.PatchMod);
                    weaponOverride.BasicStats!.Damage = newDamage;
                    patched++;
                }
                Console.WriteLine($"  StatScaler: {patched} weapons patched.");
            }

            // -----------------------------------------------------------------------
            // Armor and Shields — scale ArmorRating
            // -----------------------------------------------------------------------
            if (patchHeavy || patchLight || patchShield)
            {
                float heavyMult  = settings.HeavyArmorRatingMultiplier;
                float lightMult  = settings.LightArmorRatingMultiplier;
                float shieldMult = settings.ShieldArmorRatingMultiplier;

                if (patchHeavy)  Console.WriteLine($"  StatScaler: applying heavy armor rating multiplier ×{heavyMult:F4}");
                if (patchLight)  Console.WriteLine($"  StatScaler: applying light armor rating multiplier ×{lightMult:F4}");
                if (patchShield) Console.WriteLine($"  StatScaler: applying shield armor rating multiplier ×{shieldMult:F4}");

                int heavyPatched = 0, lightPatched = 0, shieldPatched = 0;

                foreach (var ctx in state.LoadOrder.PriorityOrder.Armor().WinningContextOverrides())
                {
                    var armor = ctx.Record;
                    if (armor.Keywords == null) continue;

                    float originalRating = armor.ArmorRating;
                    if (originalRating <= 0f) continue;

                    bool isShield = false, isHeavy = false, isLight = false;
                    foreach (var kwLink in armor.Keywords)
                    {
                        if      (kwLink.Equals(Skyrim.Keyword.ArmorShield)) { isShield = true; break; }
                        else if (kwLink.Equals(Skyrim.Keyword.ArmorHeavy))  { isHeavy  = true; }
                        else if (kwLink.Equals(Skyrim.Keyword.ArmorLight))  { isLight  = true; }
                    }

                    float mult; bool shouldPatch;
                    if      (isShield) { mult = shieldMult; shouldPatch = patchShield; }
                    else if (isHeavy)  { mult = heavyMult;  shouldPatch = patchHeavy;  }
                    else if (isLight)  { mult = lightMult;  shouldPatch = patchLight;  }
                    else continue;

                    if (!shouldPatch) continue;

                    float newRating = ClampFloat((float)Math.Round(originalRating * mult));
                    if (Math.Abs(newRating - originalRating) < 0.01f) continue;

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
            // Ammo — scale Damage
            // -----------------------------------------------------------------------
            if (patchAmmo)
            {
                float mult = settings.AmmoDamageMultiplier;
                Console.WriteLine($"  StatScaler: applying ammo damage multiplier ×{mult:F4}");

                int patched = 0;
                foreach (var ctx in state.LoadOrder.PriorityOrder.Ammunition().WinningContextOverrides())
                {
                    var ammo = ctx.Record;
                    if (ammo.Flags.HasFlag(Ammunition.Flag.NonPlayable)) continue;

                    float originalDamage = ammo.Damage;
                    if (originalDamage <= 0f) continue;

                    float newDamage = ClampFloat((float)Math.Round(originalDamage * mult, 2));
                    if (Math.Abs(newDamage - originalDamage) < 0.001f) continue;

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
