# SkyrimReleveler

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition that creates a fully static, unleveled world. Enemies have fixed levels determined by their type and faction, NPCs get proper skills and perks based on what they actually use, and followers scale infinitely with the player.

## What it does

**Tier-based enemy releveling**
Classifies every NPC into one of 15 tiers (Cosmic → Vermin) based on race, faction, and keywords. Each tier maps to a level range that scales proportionally from your configured `WorldMaxLevel`. Within each tier, NPCs are positioned using their peer group — faction members, race peers, or EditorID stem groups — so relative strength is preserved. Mod-added NPCs are handled automatically.

**Named NPC overrides**
Important named characters (dragon priests, quest bosses, faction leaders, etc.) get exact fixed levels via `named_npcs.json`. Over 150 vanilla and DLC named NPCs are preconfigured.

**CalcMaxLevel preservation**
If a mod has already assigned an NPC a higher level cap than the pipeline would compute, the patcher respects it and leaves that NPC alone.

**NPC class rebuild**
Each NPC gets a new class record generated from their actual weapons, armor, and spells. Vendor and crafter NPCs are excluded.

**Skill redistribution**
NPC skill values are recalculated based on their level and rebuilt class weights. A level 80 warrior will have high One-Handed and Block; a level 80 mage will have high Destruction and Alteration.

**Perk distribution**
NPCs receive perks appropriate to their skill levels, respecting skill requirements and prerequisites. Supports vanilla perks and major perk overhauls including Ordinator, Vokrii, SPERG, Adamant, and Path of Sorcery. Player-only perks (crafting menus, economy, UI-driven powers) are excluded via `excludedPerks.json`.

**Unlimited follower scaling**
Followers in vanilla follower factions plus a configurable custom list scale 1:1 with the player with no level cap. Companions that don't use the standard follower faction can be added to `customFollowers.json` by EditorID keyword.

**Race level modifiers**
Per-race level multipliers and additive offsets, configurable in `raceLevelModifiers.json`.

**Boss bonuses**
NPCs with EditorIDs matching configured keywords (Boss, Chief, Warlord, Briarheart, etc.) get a percentage level bonus on top of their computed level.

## Requirements

- [Synthesis](https://github.com/Mutagen-Modding/Synthesis) v0.36+
- Skyrim Special Edition

## Installation

1. Add the patcher to Synthesis via the GitHub repository URL or local path
2. Run it — default data files are written to `Skyrim/Data/Skyrim Releveler/` automatically on first run
3. Place `synthesis.esp` at the end of your load order

No other unleveling patchers needed. SkyrimReleveler reads original NPC data directly from your full load order.

## Configuration

All settings are in the Synthesis UI with hover tooltips.

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

## Data Files

Default data files are seeded automatically on first run. They live in `Skyrim/Data/Skyrim Releveler/` and can be edited freely — the patcher will never overwrite your changes.

| File | Purpose |
|------|---------|
| `named_npcs.json` | EditorID token → fixed level for specific named NPCs |
| `customFollowers.json` | Mod-added follower EditorID keywords for unlimited scaling |
| `excludedNPCs.json` | NPCs to skip entirely (by EditorID keyword) |
| `excludedPerks.json` | Perks never distributed to NPCs |
| `raceLevelModifiers.json` | Per-race level multipliers and additive offsets |

### Named NPC overrides

Keys are matched as CamelCase token substrings against EditorIDs (case-insensitive):

```json
{
    "DLC2GeneralCarius": 400,
    "Miraak":            600,
    "Arngeir":           400,
    "Harkon":            500
}
```

### Custom followers

Companions that don't use vanilla follower factions get unlimited scaling when added here:

```json
{
    "Followers": [
        { "Key": "Inigo",   "ForbiddenKeys": [] },
        { "Key": "MM_Ashe", "ForbiddenKeys": [] }
    ]
}
```

## Perk overhaul support

The patcher distributes perks by walking each NPC's actual perk tree — whatever overhaul you have loaded is what gets distributed. The `excludedPerks.json` ships with a curated list covering player-only perks from Ordinator, SPERG, Vokrii, Adamant, Path of Sorcery, and vanilla DLC. Everything passive — damage bonuses, elemental perks, armor, stagger, bleed, summon buffs, wards — is left in.

## Mid-playthrough updates

If you update the patcher on an existing save, NPCs already loaded in your save retain their old cached level. To force a refresh on a specific NPC, select them in the console and run:

```
setlevel 1000 1 1 1000
```

This forces the game to recalculate their level from the current record.

## Compatibility

Works with any mod that uses standard NPC records. Does not require True Unleveled Skyrim or any other unleveling mod.

## Credits

Skill redistribution, class rebuild, and perk distribution logic originally inspired by [True Unleveled Skyrim](https://github.com/ReaperAnon/True-Unleveled-Skyrim) by ReaperAnon (GPL-3).

## License

GPL-3.0 — see [LICENSE](LICENSE)
