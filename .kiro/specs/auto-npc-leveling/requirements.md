# Requirements Document

## Introduction

The **auto-npc-leveling** feature replaces the current manual faction-based leveling system in the SkyrimReleveler Synthesis patcher. Instead of hand-authored `[min, max]` ranges per faction EditorID stored in `enemy_rules.json`, every NPC is assessed automatically through a layered signal pipeline that assigns it to one of 15 tiers and computes a final static level from a weighted score derived from vanilla level, equipment/spell complexity, and CalcMaxLevel. The existing patcher subsystems (follower scaling, perk distribution, skill redistribution, class rebuild, `named_npcs.json` overrides, race modifiers, boss keyword bonus, exclusion lists) remain unchanged.

---

## Glossary

- **Patcher**: The SkyrimReleveler Synthesis patcher, implemented in `Program.cs`.
- **Assessment Pipeline**: The new automatic process that classifies each NPC, assigns a tier, and computes a final level without any hand-authored faction rules.
- **Tier**: One of 15 ordered combat-power bands (0–14), each with a defined `[TierMin, TierMax]` level range. Tier 0 is the most powerful; Tier 14 is the weakest.
- **WorldMaxLevel**: The absolute level ceiling used when computing tier bounds. Default value: 1000.
- **NPC_Iterator**: The component responsible for iterating all NPCs via `state.LoadOrder.PriorityOrder.Npc().WinningOverrides()`.
- **Classifier**: The component that evaluates race keywords, race EditorID patterns, NPC EditorID patterns, NPC display-name patterns, and NPC keywords to determine a tier for a given NPC.
- **Peer_Group**: The set of NPCs used to compute the vanilla-level percentile for a given NPC (faction peers → race peers → tier-global fallback).
- **Score_Calculator**: The component that combines the three weighted signals (vanilla-level percentile, equipment/spell complexity, CalcMaxLevel percentile) into a position within a tier range.
- **Civilian_Detector**: The component that identifies non-combat NPCs (unique named traders, crafters, bards, etc.) and routes them to PC-level scaling instead of the tier system.
- **AutoLevelingMode**: Active when `Settings.EnableAutoLeveling` is `true`. When `false`, the Patcher falls back to the legacy `enemy_rules.json` behavior.
- **RaceKeyword**: A keyword attached to a Race record (e.g., `ActorTypeDragon`, `ActorTypeNPC`).
- **BossBonus**: The existing `GetBonusPercent` mechanism that adds a percentage boost to an NPC's final level based on EditorID keyword matches.
- **RaceModifier**: The existing `raceLevelModifiers.json` mechanism that applies a `mult + add` adjustment to an NPC's final computed level.
- **NamedOverride**: An absolute level value from `named_npcs.json` that takes highest priority over all other level computations.
- **EquipmentScore**: A normalized [0, 1] weight derived from the existing class-rebuild skill-weight map, reflecting the complexity of an NPC's weapons, armor, and spells.
- **CalcMaxLevel**: The value stored in `NpcConfiguration.CalcMaxLevel`; used as one scoring signal.
- **FlatFaction**: A faction whose all members share a single vanilla level (current concept retained for peer grouping).
- **AutoFactionMinPeers**: Minimum number of peers required for faction-level peer comparison to apply. Default: 3.

---

## Requirements

### Requirement 1: NPC Iteration

**User Story:** As a mod author, I want the patcher to assess every NPC from the full load order so that no enemy is left at its vanilla level when auto-leveling is active.

#### Acceptance Criteria

1. WHEN `Settings.EnableAutoLeveling` is `true`, THE `NPC_Iterator` SHALL iterate all NPCs via `state.LoadOrder.PriorityOrder.Npc().WinningOverrides()`, identical to the current iteration logic.
2. WHILE iterating NPCs, THE `NPC_Iterator` SHALL skip NPCs that are members of the `npcsToIgnore` hard-coded set.
3. WHILE iterating NPCs, THE `NPC_Iterator` SHALL skip NPCs whose `Configuration.Flags` includes the `IsCharGenFacePreset` flag.
4. WHILE iterating NPCs, THE `NPC_Iterator` SHALL skip NPCs that have the `PlayerKeyword` keyword.
5. WHILE iterating NPCs, THE `NPC_Iterator` SHALL skip NPCs whose EditorID matches an entry in `excludedNPCs.json` according to the existing `IsExcluded` logic.
6. WHILE iterating NPCs, THE `NPC_Iterator` SHALL allow additional skip conditions to be applied beyond the listed criteria without treating the absence of a match as an error.

---

### Requirement 2: Named NPC Override Priority

**User Story:** As a mod author, I want `named_npcs.json` overrides to remain the highest-priority assignment so that curated NPC levels are never overwritten by the automatic system.

#### Acceptance Criteria

1. WHEN a `NamedOverride` exists for an NPC's EditorID, THE `Patcher` SHALL assign that NPC the level from `named_npcs.json`, bypassing the `Assessment Pipeline` entirely.
2. WHEN a `NamedOverride` level is applied, THE `Patcher` SHALL still apply the `RaceModifier` and `Settings.GlobalOffset` to the named level, matching the current behavior.
3. THE `Patcher` SHALL resolve `NamedOverride` keys using the existing token-split (`SplitTokens`) matching algorithm.

---

### Requirement 3: Civilian Detection

**User Story:** As a mod author, I want non-combat unique NPCs (merchants, crafters, bards, etc.) to retain PC-level scaling so that they do not become combat threats in the world.

#### Acceptance Criteria

1. WHEN an NPC's race has the `ActorTypeNPC` RaceKeyword AND the NPC has the `Unique` flag AND the NPC's class EditorID or class Name contains any of the civilian class terms (`smith`, `alchem`, `enchant`, `vendor`, `apothec`, `innkeep`, `merchant`, `farmer`, `miner`, `beggar`, `priest`, `bard`, `jarl`, `steward`), THE `Civilian_Detector` SHALL classify that NPC as a civilian.
2. WHEN an NPC is classified as a civilian, THE `Patcher` SHALL assign PC-level scaling with `LevelMult = 1.0` and a `CalcMaxLevel` chosen uniformly at random from the range [10, 50].
3. WHEN an NPC is classified as a civilian, THE `Patcher` SHALL NOT run that NPC through the `Assessment Pipeline` tier system.
4. WHEN an NPC is classified as a civilian, THE `Patcher` SHALL apply class rebuild, skill redistribution, AND perk distribution to that NPC as a complete package; all three processes are mandatory and SHALL NOT be individually skipped.

---

### Requirement 4: Follower Detection

**User Story:** As a mod author, I want followers to keep unlimited PC-level scaling so that companions remain effective regardless of game progression.

#### Acceptance Criteria

1. WHEN `Settings.ScaleFollowers` is `true` AND an NPC belongs to `PotentialFollowerFaction` or `PotentialHireling`, OR matches a `customFollowers.json` entry, THE `Patcher` SHALL assign that NPC unlimited PC-level scaling (`LevelMult = 1.0`, `CalcMaxLevel = short.MaxValue`).
2. WHEN a follower is detected, THE `Patcher` SHALL NOT run that NPC through the `Assessment Pipeline`.

---

### Requirement 5: Race Keyword Classification

**User Story:** As a mod author, I want the classifier to determine an NPC's broad category from its race's keywords so that classification works even for mod-added NPCs with no faction.

#### Acceptance Criteria

1. WHEN the `Classifier` evaluates an NPC, THE `Classifier` SHALL resolve the NPC's race record and check for the following RaceKeywords in this priority order: `ActorTypeDragon`, `ActorTypeDaedra`, `ActorTypeUndead`, `ActorTypeDwarven`, `ActorTypeWerewolf`, `ActorTypeNPC`, `ActorTypeAnimal`, `ActorTypeCreature`.
2. WHEN a RaceKeyword is matched, THE `Classifier` SHALL assign the corresponding broad category (Dragon, Daedra, Undead, Construct, Werewolf, Humanoid, Animal, Creature) and SHALL NOT continue checking lower-priority keywords.
3. IF an NPC's race record cannot be resolved, THEN THE `Classifier` SHALL assign the Humanoid broad category as a safe default.

---

### Requirement 6: Race EditorID Sub-Tier Refinement

**User Story:** As a mod author, I want race EditorID pattern matching to refine broad categories into sub-tiers so that, for example, Alduin is placed in a higher tier than a standard dragon.

#### Acceptance Criteria

1. WHEN the broad category is Dragon, THE `Classifier` SHALL check the race EditorID using case-insensitive contains matching: `AlduinRace` maps to Tier 1 (World-ender); a race EditorID containing `DragonPriest` maps to Tier 3 (Ancient Lich / Dragon Priest); all other Dragon races map to Tier 2 (Dragon).
2. WHEN the broad category is Daedra, THE `Classifier` SHALL check the race EditorID: a race EditorID containing any of `Dremora`, `Xivilai`, `Xivkyn`, `GoldenSaint`, `DarkSeducer` maps to Tier 4 (High Daedra); a race EditorID containing any of `Scamp`, `Clannfear`, `Daedroth` maps to Tier 7 (Falmer / Mid Daedra / Giant). WHEN a Daedra race EditorID contains patterns that match multiple tiers simultaneously, THE `Classifier` SHALL assign the highest-priority tier (the tier with the lowest tier number, i.e., most powerful).
3. WHEN the broad category is Undead, THE `Classifier` SHALL check the race EditorID: a race EditorID containing any of `VampireLord`, `VampireBeast`, `SoulCairn` maps to Tier 5 (Vampire Lord / Soul Cairn); a race EditorID containing `Lich` maps to Tier 3 (Ancient Lich / Dragon Priest); a race EditorID containing `DraugrJyrik` or `HulkingDraugr` maps to Tier 9 (Draugr / Atronach / Skeleton Lord); other Undead races map to Tier 9 (Draugr / Atronach / Skeleton Lord).
4. WHEN the broad category is Construct, THE `Classifier` SHALL check the race EditorID: a race EditorID containing any of `Centurion`, `Forgemaster`, `Colossus`, `Golem` maps to Tier 6 (Elite Construct); other Construct races map to Tier 9 (Draugr / Atronach / Skeleton Lord).
5. WHEN the broad category is Humanoid, THE `Classifier` SHALL check the race EditorID: a race EditorID containing `SnowElf` or `Falmer` maps to Tier 7 (Falmer / Mid Daedra / Giant); all other Humanoid races map to Tier 12 (Humanoid Enemy).
6. WHEN the broad category is Werewolf, THE `Classifier` SHALL assign Tier 8 (Vampire / Gargoyle / Werewolf).
7. WHEN the broad category is Animal, THE `Classifier` SHALL assign Tier 14 (Vermin / Passive Animal).
8. WHEN the broad category is Creature, THE `Classifier` SHALL check the race EditorID: a race EditorID containing any of `Mammoth`, `Giant`, `Troll`, `Hagrave`, `Spriggan`, `Chaurus`, `Falmer` maps to Tier 7 (Falmer / Mid Daedra / Giant); a race EditorID containing any of `Bear`, `Sabrecat`, `Wolf`, `Spider` maps to Tier 11 (Dangerous Wildlife); all other Creature races map to Tier 13 (Standard Creature).

---

### Requirement 7: NPC EditorID and Display Name Signal Overrides

**User Story:** As a mod author, I want the classifier to check the NPC's own EditorID and display name for high-signal patterns so that exceptional NPCs like Molag Bal or named Dragon Priests are correctly elevated regardless of their race record.

#### Acceptance Criteria

1. WHEN the NPC's EditorID or display name contains any of `MolagBal`, `Jyggalag`, `Shoggoth`, `Daedric Prince` (case-insensitive), THE `Classifier` SHALL override the tier to Tier 0 (Cosmic).
2. WHEN the NPC's EditorID or display name contains any of `DragonPriest`, `Lich`, `Necromancer` (case-insensitive) AND the NPC has not already been assigned Tier 0 or Tier 1, THE `Classifier` SHALL elevate the NPC to Tier 3 (Ancient Lich / Dragon Priest).
3. WHEN the NPC's EditorID or display name contains `Vampire` (case-insensitive) AND the NPC was not classified by race keyword as Daedra, Dragon, or a higher undead sub-tier, THE `Classifier` SHALL assign the NPC a vampire modifier that maps to Tier 8 (Vampire / Gargoyle / Werewolf).
4. WHEN the NPC's EditorID or display name contains any of `Zombie`, `Undead`, `Ghost`, `Wraith`, `Shade` (case-insensitive) AND the current assigned tier is lower priority than Tier 9, THE `Classifier` SHALL adjust the NPC to Tier 9 (Draugr / Atronach / Skeleton Lord).
5. WHEN the NPC's EditorID or display name contains any of `Golem`, `Construct`, `Automaton`, `Centurion` (case-insensitive) AND the current assigned tier is lower priority than Tier 6, THE `Classifier` SHALL adjust the NPC to Tier 6 (Elite Construct).

---

### Requirement 8: NPC Keyword Signal Overrides

**User Story:** As a mod author, I want the classifier to check keywords on the NPC record itself so that vampire and related modifiers are applied even when the race keyword is absent.

#### Acceptance Criteria

1. WHEN the NPC has the `Vampire` keyword AND the current assigned tier is lower priority than Tier 8, THE `Classifier` SHALL elevate the NPC by one tier step within the vampire/undead tier group (at minimum Tier 8).
2. WHEN the NPC has the `DLC1_IS_Vampire` keyword AND the current assigned tier number is greater than 5 (i.e., less powerful than Tier 5), THE `Classifier` SHALL elevate the NPC to at least Tier 5 (Vampire Lord / Soul Cairn); if the current tier is already Tier 5 or a more powerful tier (lower tier number), THE `Classifier` SHALL leave the tier unchanged. WHERE the NPC's other signals have already placed it at a tier more powerful than Tier 5, THE `Classifier` SHALL allow that higher tier to stand.

---

### Requirement 9: Tier System Definition

**User Story:** As a mod author, I want a clearly defined 15-tier system with documented level ranges so that I can predict and tune the output levels for any NPC category.

#### Acceptance Criteria

1. THE `Patcher` SHALL define exactly 15 tiers numbered 0 through 14 with the following `[TierMin, TierMax]` level ranges, where `WorldMaxLevel` defaults to 1000:

   | Tier | Name                              | TierMin | TierMax |
   |------|-----------------------------------|---------|---------|
   | 0    | Cosmic                            | 800     | 1000    |
   | 1    | World-ender                       | 600     | 800     |
   | 2    | Dragon                            | 350     | 600     |
   | 3    | Ancient Lich / Dragon Priest      | 250     | 450     |
   | 4    | High Daedra                       | 200     | 380     |
   | 5    | Vampire Lord / Soul Cairn         | 160     | 300     |
   | 6    | Elite Construct                   | 140     | 260     |
   | 7    | Falmer / Mid Daedra / Giant       | 100     | 200     |
   | 8    | Vampire / Gargoyle / Werewolf     | 80      | 180     |
   | 9    | Draugr / Atronach / Skeleton Lord | 60      | 140     |
   | 10   | Spriggan / Hagrave / Troll        | 45      | 110     |
   | 11   | Dangerous Wildlife                | 30      | 80      |
   | 12   | Humanoid Enemy                    | 15      | 120     |
   | 13   | Standard Creature                 | 10      | 60      |
   | 14   | Vermin / Passive Animal           | 1       | 20      |

2. THE `Patcher` SHALL scale `TierMin` and `TierMax` values proportionally when `Settings.WorldMaxLevel` differs from the default of 1000, so that the tier boundaries are always expressed as a fraction of `WorldMaxLevel`.
3. THE `Score_Calculator` SHALL produce a final level within `[TierMin, TierMax]` for each NPC; the result SHALL NOT exceed `Settings.WorldMaxLevel` before race modifiers and boss bonus are applied.

---

### Requirement 10: Peer Group Construction

**User Story:** As a mod author, I want the peer group used for percentile scoring to reflect an NPC's actual context in the load order so that levels are consistent relative to similar enemies.

#### Acceptance Criteria

1. THE `Patcher` SHALL build peer groups during a single pre-pass over all eligible NPCs before computing final levels.
2. WHEN an NPC shares at least one hostile faction EditorID with `Settings.AutoFactionMinPeers` or more other eligible NPCs, THE `Patcher` SHALL use those NPCs as the faction peer group for that NPC.
3. IF an NPC has no faction peer group with at least `Settings.AutoFactionMinPeers` members, THEN THE `Patcher` SHALL use all eligible NPCs sharing the same resolved race FormKey as the race peer group, provided that at least one such race peer exists.
4. IF an NPC has neither a qualifying faction peer group nor at least one race peer, THEN THE `Patcher` SHALL use all eligible NPCs assigned to the same tier as the tier-global fallback peer group.
5. THE `Patcher` SHALL compute the vanilla-level percentile of each NPC within its selected peer group using the `GetEffectiveLevel` function, consistent with how effective levels are obtained today.

---

### Requirement 11: Weighted Signal Score

**User Story:** As a mod author, I want the final level within a tier range to reflect both the NPC's vanilla relative strength and its equipment/spell loadout so that powerful variants naturally end up at the high end of their tier.

#### Acceptance Criteria

1. THE `Score_Calculator` SHALL compute the final intra-tier position using a weighted sum of three signals:
   - 40%: vanilla-level percentile of the NPC within its `Peer_Group` (value in [0, 1]).
   - 30%: `EquipmentScore` — the normalized skill-weight complexity score derived from the existing `PopulateByInventory`, `PopulateBySpells`, and `PopulateByOutfit` methods used in class rebuild, normalized to [0, 1] across all NPCs.
   - 30%: `CalcMaxLevel` percentile of the NPC within its `Peer_Group` (value in [0, 1]; treated as 0 when `CalcMaxLevel` is 0 or absent).
2. THE `Score_Calculator` SHALL map the weighted score (value in [0, 1]) to the tier's `[TierMin, TierMax]` range using linear interpolation: `FinalLevel = round(TierMin + score × (TierMax − TierMin))`.
3. WHEN all three signal values are unavailable for an NPC, THE `Score_Calculator` SHALL strictly enforce a score of 0.5 as the neutral default, placing the NPC at the midpoint of its tier range; this default SHALL NOT be substituted by any alternative calculation.
4. WHEN only some signals are available for an NPC, THE `Score_Calculator` SHALL normalize the weighted sum so that the combined available-signal weights sum to 1.0 before mapping to the tier range, ensuring the result always stays within [0, 1].

---

### Requirement 12: Post-Score Adjustments

**User Story:** As a mod author, I want race modifiers and boss bonuses to apply on top of the tier-computed level so that existing tuning knobs continue to work in the new system.

#### Acceptance Criteria

1. AFTER the `Score_Calculator` produces a base level, THE `Patcher` SHALL apply the `RaceModifier` (`mult` then `add`) from `raceLevelModifiers.json` to the base level, matching the existing application order.
2. AFTER the `RaceModifier` is applied, THE `Patcher` SHALL apply `Settings.GlobalOffset` to the level.
3. AFTER `Settings.GlobalOffset` is applied, THE `Patcher` SHALL apply the `BossBonus` via the existing `GetBonusPercent` function if `Settings.EnableBonusKeywords` is `true`.
4. THE `Patcher` SHALL clamp the final level to the range [1, `short.MaxValue`] before writing it to the NPC record using the existing `ApplyLevel` function.

---

### Requirement 13: Legacy Fallback Mode

**User Story:** As a mod author, I want to be able to disable auto-leveling and revert to the old `enemy_rules.json` system so that I can compare outputs or roll back without losing the existing configuration.

#### Acceptance Criteria

1. WHEN `Settings.EnableAutoLeveling` is `false`, THE `Patcher` SHALL execute the existing faction-based leveling logic using `enemy_rules.json` without modification.
2. WHEN `Settings.EnableAutoLeveling` is `true`, THE `Patcher` SHALL NOT load or reference `enemy_rules.json` or any of the variables it populates (`orderedRules`, `factionAllLevels`, `factionSourceRange`, `flatFactions`, `flatFactionStems`, `npcFactionCache`).
3. THE `Patcher` SHALL log a console message indicating which leveling mode is active at startup.

---

### Requirement 14: Settings Additions

**User Story:** As a mod author, I want new settings exposed in the Synthesis UI so that I can tune the auto-leveling system without recompiling the patcher.

#### Acceptance Criteria

1. THE `Settings` class SHALL expose `int WorldMaxLevel` with a default value of 1000, representing the level assigned to the top of Tier 0.
2. THE `Settings` class SHALL expose `int AutoFactionMinPeers` with a default value of 3, representing the minimum faction peer group size required before faction-based peer comparison is used.
3. THE `Settings` class SHALL expose `bool EnableAutoLeveling` with a default value of `true`, acting as the master toggle between the new automatic system and the legacy `enemy_rules.json` system.
4. THE `Settings` class SHALL provide tooltip annotations for all three new settings consistent with the annotation style of the existing `Settings.cs`.

---

### Requirement 15: Preservation of Unchanged Subsystems

**User Story:** As a mod author, I want all patcher subsystems not related to the leveling assignment to continue working identically so that existing mod setups are not disrupted.

#### Acceptance Criteria

1. THE `Patcher` SHALL continue to load and apply `named_npcs.json`, `raceLevelModifiers.json`, `customFollowers.json`, `excludedNPCs.json`, and `excludedPerks.json` using the existing loading and application logic regardless of the value of `Settings.EnableAutoLeveling`.
2. THE `Patcher` SHALL continue to call `RebalanceClassValues`, `RelevelNPCSkills`, and `DistributeNPCPerks` for every NPC that passes the skip conditions, regardless of the value of `Settings.EnableAutoLeveling`.
3. THE `Patcher` SHALL continue to call `DisableExtraDamagePerks` at the end of `RunPatch` regardless of the value of `Settings.EnableAutoLeveling`.
4. THE `Patcher` SHALL continue to expose and apply `Settings.GlobalOffset`, `Settings.EnableBonusKeywords`, `Settings.BonusKeywords`, `Settings.NPCSkillsPerLevel`, `Settings.NPCMaxSkillLevel`, `Settings.RebuildNPCClasses`, `Settings.NPCPerksPerLevel`, `Settings.RemoveVanillaPerks`, `Settings.DisableExtraDamagePerks`, `Settings.OutlierPercentileCutoff`, `Settings.ScaleFollowers`, and all stem-stripping settings without modification.
5. THE `Patcher` SHALL continue to use `GetStem` for peer grouping purposes within the auto-leveling pipeline.

---

### Requirement 16: Logging and Diagnostics

**User Story:** As a mod author, I want the patcher to log classification and level computation details when debug output is enabled so that I can verify the new system is working as intended.

#### Acceptance Criteria

1. WHEN `Settings.PrintDebugOutput` is `true`, THE `Patcher` SHALL log each NPC's assigned tier, peer group type (faction / race / tier-global), weighted score components, and final computed level before post-score adjustments.
2. WHEN `Settings.PrintDebugOutput` is `true`, THE `Patcher` SHALL log a warning for each NPC whose final level (after all adjustments) exceeds 100.
3. THE `Patcher` SHALL log a summary line at the end of `RunPatch` reporting the total number of NPCs assessed by the auto-leveling pipeline, the number assigned via `NamedOverride`, the number treated as civilians, and the number treated as followers.
