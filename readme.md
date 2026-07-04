# SkyrimReleveler

A Synthesis patcher for Skyrim Special Edition that relevels enemies, redistributes NPC skills and perks, and scales followers with the player.

## Features

- **Enemy releveling** — assigns static levels to NPCs based on faction rules, proportionally mapping vanilla/mod tiers onto configurable target ranges
- **Flat faction scatter** — factions where all members share the same level (e.g. dragons) are split into stem-based bands so different enemy types land at different levels
- **Named NPC overrides** — specific named NPCs get hand-tuned fixed levels via `named_npcs.json`
- **Unlimited follower scaling** — followers scale 1:1 with the player with no level cap, covering both vanilla follower factions and mod-added companions via `customFollowers.json`
- **NPC class rebuild** — each NPC gets a new class record generated from their actual weapons, armor, and spells, so skill points go to relevant skills
- **Skill redistribution** — NPC skills are recalculated based on their level and rebuilt class
- **Perk distribution** — NPCs receive perks appropriate to their skill levels, respecting prerequisites
- **Race level modifiers** — configurable additive and multiplicative level adjustments per race
- **Boss keyword bonus** — NPCs matching configurable EditorID keywords (Boss, Chief, etc.) receive a level bonus
- **Extra damage perk disabling** — optionally clears crExtraDamage perks that inflate NPC damage

## Data Files

| File | Purpose |
|------|---------|
| `enemy_rules.json` | Faction EditorID (partial match) → `[targetMin, targetMax]` level range |
| `named_npcs.json` | Named NPC EditorID token → fixed level |
| `customFollowers.json` | Mod-added follower EditorID keywords |
| `excludedNPCs.json` | NPCs to skip entirely (by EditorID keyword) |
| `excludedPerks.json` | Perks never distributed to NPCs |
| `raceLevelModifiers.json` | Race-based level multipliers and additive offsets |

## Load Order

Place SkyrimReleveler as a standalone patcher. It reads original NPC levels directly — no other unleveling patcher needed before it.

## Requirements

- [Synthesis](https://github.com/Mutagen-Modding/Synthesis)
- Skyrim Special Edition
