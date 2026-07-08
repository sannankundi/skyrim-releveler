# SkyrimReleveler

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition that creates a fully static, unleveled world. Enemies have fixed levels determined by their type and faction, NPCs get proper skills and perks based on what they actually use, and followers scale infinitely with the player.

## What it does

**Tier-based enemy releveling**
Classifies every NPC into one of 15 tiers (Cosmic → Vermin) based on race, faction, and keywords. Each tier maps to a level range that scales proportionally from your configured `WorldMaxLevel`. Within each tier, NPCs are positioned using their peer group — faction members, race peers, or EditorID stem groups — so relative strength is preserved. Mod-added NPCs are handled automatically.

**Named NPC overrides**
Important named characters (dragon priests, quest bosses, faction leaders, etc.) get exact fixed levels via `named_npcs.json`. Over 150 vanilla and DLC named NPCs are preconfigured. General Falx Carius, Miraak, Harkon, Arngeir, and dozens more have carefully tuned levels.

**CalcMaxLevel preservation**
If a mod has already assigned an NPC a higher level cap than the pipeline would compute, the patcher respects it and does not downgrade it. Mod-added companions with high caps retain them.

**NPC class rebuild**
Each NPC gets a new class record generated from their actual weapons, armor, and spells — so a sword-and-shield bandit gets combat/block skills, not generic stats. Vendor and crafter NPCs are excluded.

**Skill redistribution**
NPC skill values are recalculated based on their level and rebuilt class weights. A level 80 warrior will have high One-Handed and Block; a level 80 mage will have high Destruction and Alteration.

**Perk distribution**
NPCs receive perks appropriate to their skill levels, respecting skill level requirements and perk prerequisites. A large exclusion list prevents player-only perks (crafting, economy, UI-driven powers) from being assigned to NPCs. Supports vanilla perks and major perk overhauls including Ordinator, Vokrii, SPERG, Adamant, and Path of Sorcery.

**Unlimited follower scaling**
Followers in vanilla follower factions plus a configurable custom list scale 1:1 with the player with no level cap. Custom followers are registered by EditorID keyword in `customFollowers.json` — useful for mod companions like Inigo, Sofia, Serana, and others that don't use standard follower factions.

**Race level modifiers**
Per-race level multipliers and additive offsets, configurable in `raceLevelModifiers.json`.

**Boss bonuses**
NPCs with EditorIDs matching configured keywords (Boss, Chief, Warlord, Briarheart, etc.) receive a percentage level bonus on top of their computed level.

**Data file sync**
Optional setting to automatically copy your edited JSON data files from your project folder into Skyrim's Data folder before each run, so you never have to manually copy files after making changes.

## Requirements

- [Synthesis](https://github.com/Mutagen-Modding/Synthesis) v0.36+
- Skyrim Special Edition

## Installation

1. Add the patcher to Synthesis via the GitHub repository URL or local path
2. Run it — data files are written to `Skyrim/Data/Skyrim Releveler/` automatically
3. Place `synthesis.esp` at the end of your load order for best results

No other unleveling patchers needed. SkyrimReleveler reads original NPC data directly from your full load order.

## Configuration

All settings are accessible through the Synthesis UI with hover tooltips.

| Setting | Default | Description |
|---------|---------|-------------|
| `GlobalOffset` | 0 | Flat level added to every NPC's final level |
| `WorldMaxLevel` | 1000 | Level ceiling for Tier 0 (Cosmic). All tier ranges scale from this. |
| `ScaleFollowers` | true | Followers scale infinitely with the player |
| `RebuildNPCClasses` | true | Rebuilds class from actual equipment and spells |
| `NPCSkillsPerLevel` | 2.5 | Skill points distributed per NPC level |
| `NPCPerksPerLevel` | 0.25 | Perk points distributed per NPC level |
| `RemoveVanillaPerks` | true | Strips vanilla ESM perks before redistributing |
| `DisableExtraDamagePerks` | false | Disables crExtraDamage perk inflation |
| `SyncDataFiles` | true | Copies JSON data files from `DataSourcePath` into Skyrim's Data folder on each run |
| `DataSourcePath` | "" | Full path to your project's `Data/` folder for the sync feature |

## Data Files

All data files live in `Skyrim/Data/Skyrim Releveler/`. Edit them freely — use `SyncDataFiles` + `DataSourcePath` to push changes on each run automatically.

| File | Purpose |
|------|---------|
| `named_npcs.json` | EditorID token → fixed level for specific named NPCs |
| `customFollowers.json` | Mod-added follower EditorID keywords for unlimited scaling |
| `excludedNPCs.json` | NPCs to skip entirely (by EditorID keyword) |
| `excludedPerks.json` | Perks never distributed to NPCs (player-only, UI-driven, economy) |
| `raceLevelModifiers.json` | Per-race level multipliers and additive offsets |

### Named NPC overrides

Edit `named_npcs.json` to set exact levels for specific NPCs. Keys are matched as token substrings against EditorIDs (CamelCase-split, case-insensitive):

```json
{
    "DLC2GeneralCarius": 400,
    "Miraak":            600,
    "Arngeir":           400,
    "Harkon":            500
}
```

### Custom followers

Edit `customFollowers.json` to register mod companions that don't use vanilla follower factions. They receive unlimited PC-scaling with no level cap:

```json
{
    "Followers": [
        { "Key": "Inigo",   "ForbiddenKeys": [] },
        { "Key": "MM_Ashe", "ForbiddenKeys": [] }
    ]
}
```

### Excluded perks

Edit `excludedPerks.json` to block specific perks from ever being distributed to NPCs. Uses the same partial EditorID matching:

```json
{
    "Keys": ["TimedBlock", "VancianMagic", "DragonHoard"],
    "ForbiddenKeys": []
}
```

## Perk overhaul support

The patcher distributes perks by walking each NPC's actual perk tree. If you use a perk overhaul it will naturally distribute those perks since it reads from your load order. The `excludedPerks.json` ships with a curated exclusion list covering player-only perks from:

- **Ordinator** — timed block chain, activate-target powers, crafting labs, Vancian Magic, Home Mythal, Dimension Door, lockpicking actives
- **SPERG** — Ethereal Arrows, Survivalist, lockdown/hotwire, crime perks, invisibility powers
- **Vokrii** — SpeakWithAnimals, economy perks, EscapeArtist, HethothsEscape
- **Adamant** — once-per-day revival (Renewal), shrine-dependent perks
- **Path of Sorcery** — BloodMage/Siphon self-drain chain, GhostDance, Decoy, Arise
- **Vanilla DLC** — vampire lord tree, werewolf totem perks

Everything else — passive damage bonuses, elemental perks, armor bonuses, stagger, bleed, summon buffs, ward perks — is left in so NPCs genuinely benefit.

## Compatibility

Works with any mod that uses standard NPC records. Mod-added NPCs are automatically classified by race and faction. Does not require True Unleveled Skyrim or any other unleveling mod — it replaces that functionality entirely.

## Credits

Skill redistribution, class rebuild, and perk distribution logic originally inspired by [True Unleveled Skyrim](https://github.com/ReaperAnon/True-Unleveled-Skyrim) by ReaperAnon (GPL-3).

## License

GPL-3.0 — see [LICENSE](LICENSE)
