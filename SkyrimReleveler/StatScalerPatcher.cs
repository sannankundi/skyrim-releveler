using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
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
    ///
    /// Template-chaining: unique items like Harkon's Sword and Auriel's Bow store their
    /// damage/rating on a template record rather than their own record. Skyrim reads the
    /// stat from the root of the template chain, so we follow CNAM/TNAM links and patch
    /// the root. Template roots that have already been patched are tracked to avoid
    /// applying the multiplier twice when multiple uniques share the same template.
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
            //
            // For weapons that use a template (CNAM field), Skyrim reads the damage
            // value from the root template record, not from the weapon record itself.
            // We walk the template chain to find the root and patch that instead.
            // Already-patched roots are tracked so the multiplier is never applied twice.
            // -----------------------------------------------------------------------
            if (patchWeapons)
            {
                float mult = settings.WeaponDamageMultiplier;
                Console.WriteLine($"  StatScaler: applying weapon damage multiplier ×{mult:F4}");

                // Track FormKeys of template roots we've already written to the patch mod,
                // so a multiplier is never applied more than once to the same record.
                var patchedWeaponRoots = new HashSet<FormKey>();

                int patched = 0;
                int templatePatched = 0;

                foreach (var ctx in state.LoadOrder.PriorityOrder.Weapon().WinningContextOverrides())
                {
                    var weapon = ctx.Record;

                    if (weapon.BasicStats == null)
                        continue;

                    // --- Follow the template chain ---
                    // If this weapon has a CNAM template, the damage Skyrim uses lives on
                    // the template root, not here. Resolve the full chain and work on the root.
                    if (!weapon.Template.IsNull)
                    {
                        FormKey rootKey = ResolveWeaponTemplateRoot(weapon, state.LinkCache);

                        // Skip if we already patched this root via another weapon record
                        if (patchedWeaponRoots.Contains(rootKey))
                            continue;

                        if (!state.LinkCache.TryResolve<IWeaponGetter>(rootKey, out var rootRecord))
                            continue;

                        if (rootRecord.BasicStats == null)
                            continue;

                        ushort rootDamage = rootRecord.BasicStats.Damage;
                        if (rootDamage == 0)
                            continue;

                        ushort newRootDamage = ClampUshort((int)Math.Round(rootDamage * mult));
                        if (newRootDamage == rootDamage)
                            continue;

                        // Write the override for the root record
                        var rootOverride = state.PatchMod.Weapons.GetOrAddAsOverride(rootRecord);
                        rootOverride.BasicStats!.Damage = newRootDamage;

                        patchedWeaponRoots.Add(rootKey);
                        templatePatched++;
                        continue;
                    }

                    // --- Non-template weapon: patch normally ---
                    // Guard: don't re-patch a record that was already handled above as
                    // someone else's template root.
                    if (patchedWeaponRoots.Contains(weapon.FormKey))
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

                    patchedWeaponRoots.Add(weapon.FormKey);
                    patched++;
                }

                Console.WriteLine($"  StatScaler: {patched} weapons patched ({templatePatched} via template root).");
            }

            // -----------------------------------------------------------------------
            // Armor and Shields — scale ArmorRating
            // Shields: ArmorShield keyword. Heavy/light: ArmorHeavy / ArmorLight.
            //
            // Same template-chaining logic as weapons: armors like Auriel's Shield store
            // their rating on a template record (TemplateArmor / TNAM field).
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
                int templateArmorPatched = 0;

                var patchedArmorRoots = new HashSet<FormKey>();

                foreach (var ctx in state.LoadOrder.PriorityOrder.Armor().WinningContextOverrides())
                {
                    var armor = ctx.Record;

                    // --- Follow the template chain ---
                    if (!armor.TemplateArmor.IsNull)
                    {
                        FormKey rootKey = ResolveArmorTemplateRoot(armor, state.LinkCache);

                        if (patchedArmorRoots.Contains(rootKey))
                            continue;

                        if (!state.LinkCache.TryResolve<IArmorGetter>(rootKey, out var rootRecord))
                            continue;

                        if (rootRecord.Keywords == null)
                            continue;

                        float originalRating = rootRecord.ArmorRating;
                        if (originalRating <= 0f)
                            continue;

                        (float rootMult, bool shouldPatch) = GetArmorMultiplier(
                            rootRecord, heavyMult, lightMult, shieldMult,
                            patchHeavy, patchLight, patchShield);

                        if (!shouldPatch)
                            continue;

                        float newRating = ClampFloat((float)Math.Round(originalRating * rootMult));
                        if (Math.Abs(newRating - originalRating) < 0.01f)
                            continue;

                        var rootOverride = state.PatchMod.Armors.GetOrAddAsOverride(rootRecord);
                        rootOverride.ArmorRating = newRating;

                        patchedArmorRoots.Add(rootKey);
                        templateArmorPatched++;
                        continue;
                    }

                    // --- Non-template armor: patch normally ---
                    if (patchedArmorRoots.Contains(armor.FormKey))
                        continue;

                    if (armor.Keywords == null)
                        continue;

                    float armorOriginalRating = armor.ArmorRating;
                    if (armorOriginalRating <= 0f)
                        continue;

                    (float mult, bool patch) = GetArmorMultiplier(
                        armor, heavyMult, lightMult, shieldMult,
                        patchHeavy, patchLight, patchShield);

                    if (!patch)
                        continue;

                    float newArmorRating = ClampFloat((float)Math.Round(armorOriginalRating * mult));
                    if (Math.Abs(newArmorRating - armorOriginalRating) < 0.01f)
                        continue;

                    var armorOverride = ctx.GetOrAddAsOverride(state.PatchMod);
                    armorOverride.ArmorRating = newArmorRating;

                    patchedArmorRoots.Add(armor.FormKey);

                    bool isShieldFinal = armor.Keywords.Any(k => k.Equals(Skyrim.Keyword.ArmorShield));
                    bool isHeavyFinal  = !isShieldFinal && armor.Keywords.Any(k => k.Equals(Skyrim.Keyword.ArmorHeavy));

                    if      (isShieldFinal) shieldPatched++;
                    else if (isHeavyFinal)  heavyPatched++;
                    else                    lightPatched++;
                }

                if (patchHeavy)  Console.WriteLine($"  StatScaler: {heavyPatched}  heavy armor pieces patched.");
                if (patchLight)  Console.WriteLine($"  StatScaler: {lightPatched}  light armor pieces patched.");
                if (patchShield) Console.WriteLine($"  StatScaler: {shieldPatched} shields patched.");
                if (templateArmorPatched > 0)
                    Console.WriteLine($"  StatScaler: {templateArmorPatched} armor/shield records patched via template root.");
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

        /// <summary>
        /// Walks the weapon CNAM (Template) chain and returns the FormKey of the root —
        /// i.e. the first record in the chain that has no template of its own.
        /// Guards against circular references with a depth cap of 32.
        /// </summary>
        private static FormKey ResolveWeaponTemplateRoot(IWeaponGetter weapon, ILinkCache linkCache)
        {
            const int maxDepth = 32;
            IWeaponGetter current = weapon;

            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (current.Template.IsNull)
                    return current.FormKey;

                if (!linkCache.TryResolve<IWeaponGetter>(current.Template.FormKey, out var next))
                    return current.FormKey; // template missing — treat current as root

                current = next;
            }

            // Fallback: circular or extremely deep chain — return where we ended up
            return current.FormKey;
        }

        /// <summary>
        /// Walks the armor TemplateArmor (TNAM) chain and returns the FormKey of the root.
        /// Guards against circular references with a depth cap of 32.
        /// </summary>
        private static FormKey ResolveArmorTemplateRoot(IArmorGetter armor, ILinkCache linkCache)
        {
            const int maxDepth = 32;
            IArmorGetter current = armor;

            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (current.TemplateArmor.IsNull)
                    return current.FormKey;

                if (!linkCache.TryResolve<IArmorGetter>(current.TemplateArmor.FormKey, out var next))
                    return current.FormKey; // template missing — treat current as root

                current = next;
            }

            return current.FormKey;
        }

        /// <summary>
        /// Returns the correct multiplier and whether the armor should be patched,
        /// based on its ArmorShield / ArmorHeavy / ArmorLight keywords.
        /// Shield takes priority over heavy/light.
        /// </summary>
        private static (float Mult, bool ShouldPatch) GetArmorMultiplier(
            IArmorGetter armor,
            float heavyMult, float lightMult, float shieldMult,
            bool patchHeavy, bool patchLight, bool patchShield)
        {
            if (armor.Keywords == null)
                return (1f, false);

            bool isShield = false;
            bool isHeavy  = false;
            bool isLight  = false;

            foreach (var kwLink in armor.Keywords)
            {
                if      (kwLink.Equals(Skyrim.Keyword.ArmorShield)) { isShield = true; break; }
                else if (kwLink.Equals(Skyrim.Keyword.ArmorHeavy))  { isHeavy  = true; }
                else if (kwLink.Equals(Skyrim.Keyword.ArmorLight))  { isLight  = true; }
            }

            if      (isShield) return (shieldMult, patchShield);
            else if (isHeavy)  return (heavyMult,  patchHeavy);
            else if (isLight)  return (lightMult,  patchLight);
            else               return (1f, false); // clothing or untyped
        }
    }
}
