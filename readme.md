# SkyrimReleveler

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition that creates a fully static, unleveled world. Enemies have fixed levels determined by their faction and type, NPCs get proper skills and perks based on what they actually use, and followers scale infinitely with the player.

## What it does

**Enemy releveling**
Assigns static levels to NPCs based on configurable faction rules. Vanilla bandits, draugr, vampires, Falmer, and every other enemy type get fixed levels that reflect their actual danger tier — outlaws are weaker than marauders, deathlords are stronger than regular draugr. Works automatically with mod-added NPCs as long as they share a faction with a configured rule.

**Proportional tier mapping**
When a faction has real level variation (e.g. Forsworn — regular vs Briarheart vs Shaman), levels are proportionally mapped onto the configured target range, preserving tier relationships. When a faction is uniformly leveled (e.g. all dragons at 100 after vanilla processing), different dragon types are scattered across the target range using stem-based band assignment.

**NPC class rebuild**
Each NPC gets a new class record generated from their actual weapons, armor, and spells — so a sword-and-shield bandit gets combat/block skills, not generic stats. This makes skill distribution actually meaningful.

**Skill redistribution**
NPC skill values are recalculated based on their level and rebuilt class weights. A level 80 warrior will have high One-Handed and Block; a level 80 mage will have high Destruction and Alteration.

**Perk distribution**
NPCs receive perks appropriate to their skill levels, respecting skill level requirements and perk prerequisites. Applied to all NPCs in your load order, including mod-added ones.

**Unlimited follower scaling**
Followers (vanilla follower factions + configurable custom list) scale 1:1 with the player with no level cap. They will never become under-leveled as you progress.

**Race level modifiers**
Configurable per-race level multipliers and additive offsets. Dragons and Draugr/Vampires have adjustments by default — tunable in `raceLevelModifiers.json`.

**Boss bonuses**
NPCs with EditorIDs matching configured keywords (Boss, Chief, Warlord, etc.) receive a percentage level bonus on top of their computed level.

## Requirements

- [Synthesis](https://github.com/Mutagen-Modding/Synthesis) v0.36+
- Skyrim Special Edition

## Installation

1. Add the patcher to Synthesis via the GitHub repository URL or local path
2. Run it — data files are generated automatically on first run
3. Place `synthesis.esp` anywhere in your load order after any mods that add or modify NPC factions. I'd prefer it be last in your load order as it does nothing you might not want it to do.

No other unleveling patchers needed before it. SkyrimReleveler reads original NPC levels directly from your load order.

## Configuration

All settings are accessible through the Synthesis UI with hover tooltips. Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `GlobalOffset` | 0 | Flat level added to every NPC's computed level |
| `ScaleFollowers` | true | Followers scale infinitely with the player |
| `RebuildNPCClasses` | true | Rebuilds class from actual equipment/spells |
| `NPCSkillsPerLevel` | 2.5 | Skill points per NPC level |
| `NPCPerksPerLevel` | 0.25 | Perk points per NPC level |
| `RemoveVanillaPerks` | true | Strips vanilla perks before redistributing |
| `DisableExtraDamagePerks` | false | Disables crExtraDamage perk inflation |

## Data Files

All data files are in the patcher's `Data/` folder. They're created with defaults on first run and can be edited freely.

| File | Purpose |
|------|---------|
| `enemy_rules.json` | Faction EditorID (partial match) → `[min, max]` target level range |
| `named_npcs.json` | Specific named NPC EditorID token → fixed level |
| `customFollowers.json` | Mod-added follower EditorID keywords for scaling |
| `excludedNPCs.json` | NPCs to skip entirely (by EditorID keyword) |
| `excludedPerks.json` | Perks never distributed to NPCs |
| `raceLevelModifiers.json` | Race-based level multipliers and additive offsets |

### Adding faction rules

Edit `enemy_rules.json` — faction EditorID keys support partial matching, so `"BanditFaction"` will match any faction whose EditorID contains that string:

```json
{
    "BanditFaction": [20, 100],
    "DraugrFaction": [40, 200],
    "DragonFaction": [200, 500]
}
```

### Adding followers

Edit `customFollowers.json` to add mod companions by EditorID keyword:

```json
{
    "Followers": [
        { "Key": "Inigo", "ForbiddenKeys": [] },
        { "Key": "Sofia",  "ForbiddenKeys": [] }
    ]
}
```

## Compatibility

Works with any mod that uses standard NPC records and faction assignments. Mod-added NPCs that share factions with configured rules are automatically handled. Mod-added factions need a matching entry in `enemy_rules.json`.

**Does not require** True Unleveled Skyrim or any other unleveling mod — it replaces the NPC unleveling functionality entirely.

## Credits

Skill redistribution, class rebuild, and perk distribution logic ported from [True Unleveled Skyrim](https://github.com/ReaperAnon/True-Unleveled-Skyrim) by ReaperAnon, licensed under GPL-3.

## License

GPL-3.0 — see [LICENSE](LICENSE)
