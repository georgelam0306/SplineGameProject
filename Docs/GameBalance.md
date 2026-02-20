# Catrillion Game Balance Document

## Design Philosophy: The "Fun Curve"

This balance pass follows the principle that **"fun" is about perceived acceleration, not raw growth**:

1. **Early game (0-18 min):** Convex/accelerating growth - fast, legible progress hooks the player
2. **Midgame (18-36 min):** Inflection to linear - introduce friction, tech gates, meaningful choices
3. **Late game (36-54 min):** Concave/diminishing returns - optimization matters, veteran units feel powerful
4. **Endgame (54-60 min):** Final wave preparation - all-in defense, victory feels earned

---

## Timeline Configuration

### Wave System (1-Hour Game)

| Parameter | Old | New | Rationale |
|-----------|-----|-----|-----------|
| FramesPerDay | 3600 | 7200 | 2 min/day for 60-min game |
| HordeDayInterval | 3 | 3 | Hordes every 6 real min |
| HordeWarningFrames | 3600 | 7200 | 2 min warning |
| MiniWaveIntervalFrames | 1800 | 3600 | 1 min between mini-waves |
| HordeBaseZombieCount | 50 | 40 | Slower start, more tension |
| HordeZombiesPerWave | 25 | 20 | Gradual scaling |
| FinalWaveMultiplierPercent | 300 | 250 | Still threatening, not overwhelming |

### Wave Schedule

| Wave | Day | Time | Zombies | Total HP (approx) | Phase |
|------|-----|------|---------|-------------------|-------|
| 1 | 3 | 6m | 40 | 2,000 | Early |
| 2 | 6 | 12m | 60 | 3,000 | Early |
| 3 | 9 | 18m | 80 | 4,500 | Mid |
| 4 | 12 | 24m | 100 | 6,000 | Mid |
| 5 | 15 | 30m | 120 | 8,000 | Mid |
| 6 | 18 | 36m | 140 | 10,000 | Late |
| 7 | 21 | 42m | 160 | 12,000 | Late |
| 8 | 24 | 48m | 180 | 14,000 | Late |
| 9 | 27 | 54m | 200 | 16,000 | Endgame |
| 10 | 30 | 60m | 550 | 45,000 | FINAL |

---

## Economy Balance

### Starting Resources
- Gold: 500 (starting max)
- Wood: 200, Stone: 200, Iron: 100, Oil: 50, Food: 100
- Population: 4 (from CommandCenter)
- Power: 50 (from CommandCenter)

### Gold Generation Targets

| Phase | Target Gold/sec | Achieved By |
|-------|-----------------|-------------|
| Start | 5 | CommandCenter |
| 6 min | 15 | CC + 2 Tents + Market |
| 12 min | 30 | + Cottages, Bank unlocked |
| 30 min | 60 | Full economy, multiple Banks |
| 60 min | 100+ | Optimized, all bonuses |

### Building Costs (Balanced)

| Building | Gold | Wood | Stone | Iron | Oil | Build (frames) | Notes |
|----------|------|------|-------|------|-----|----------------|-------|
| **Housing** |
| Tent | 30 | 0 | 0 | 0 | 0 | 60 | +4 pop, 2g/s |
| Cottage | 100 | 50 | 30 | 0 | 0 | 180 | +8 pop, 4g/s, Tech 1 |
| StoneHouse | 250 | 80 | 150 | 20 | 0 | 360 | +16 pop, 10g/s |
| **Economy** |
| Sawmill | 120 | 0 | 0 | 0 | 0 | 180 | Wood extraction |
| Quarry | 180 | 80 | 0 | 0 | 0 | 240 | Stone extraction |
| IronMine | 250 | 150 | 80 | 0 | 0 | 360 | Iron extraction |
| OilRefinery | 400 | 200 | 150 | 80 | 0 | 480 | Oil extraction, Tech 2 |
| Warehouse | 180 | 120 | 80 | 0 | 0 | 300 | Storage + 15% bonus |
| Bank | 500 | 200 | 200 | 80 | 0 | 480 | 20g/s, Tech 4 |
| Market | 180 | 100 | 60 | 0 | 0 | 240 | 8g/s + 10% area |
| **Defense** |
| Wall | 15 | 10 | 0 | 0 | 0 | 60 | Basic barrier |
| Gate | 40 | 20 | 30 | 10 | 0 | 120 | Passable wall |
| Turret | 200 | 50 | 0 | 0 | 0 | 300 | 100 DPS, splash |
| SniperTower | 400 | 150 | 80 | 50 | 0 | 480 | Garrison platform, Tech 3 |
| TeslaCoil | 600 | 100 | 150 | 120 | 20 | 540 | AOE damage, Tech 5 |
| **Core** |
| Barracks | 250 | 80 | 40 | 0 | 0 | 420 | Unit training, Tech 0 |
| PowerPlant | 300 | 150 | 150 | 40 | 0 | 360 | +100 power |
| TechLab | 600 | 300 | 300 | 150 | 0 | 900 | Engineer training |
| PowerRelay | 120 | 50 | 0 | 0 | 0 | 180 | Power extension |
| **Workshops** (Unique) |
| WoodWorkshop | 250 | 0 | 0 | 0 | 0 | 420 | Wood tech research |
| Foundry | 400 | 150 | 200 | 80 | 0 | 540 | Metal tech research |
| AdvancedLab | 600 | 150 | 300 | 150 | 80 | 720 | Advanced research, Tech 3 |

---

## Unit Balance

### Unit Stats

| Unit | HP | Damage | Cooldown | DPS | Range | Speed | Armor | Pop |
|------|------|--------|----------|-----|-------|-------|-------|-----|
| Soldier | 120 | 12 | 1.0s | 12.0 | 500 | 100 | 0 | 1 |
| Ranger | 90 | 15 | 0.8s | 18.8 | 650 | 130 | 0 | 1 |
| Heavy | 250 | 30 | 1.5s | 20.0 | 200 | 60 | 8 | 2 |
| Sniper | 70 | 60 | 2.5s | 24.0 | 900 | 0 | 0 | 1 |
| Engineer | 80 | 8 | 1.0s | 8.0 | 200 | 100 | 2 | 1 |

### Unit Costs

| Unit | Gold | Wood | Stone | Iron | Oil | Build (frames) | Tech |
|------|------|------|-------|------|-----|----------------|------|
| Soldier | 120 | 0 | 0 | 0 | 0 | 420 | 0 |
| Ranger | 180 | 40 | 0 | 0 | 0 | 540 | 0 |
| Heavy | 400 | 0 | 0 | 80 | 0 | 840 | 2 |
| Sniper | 280 | 60 | 0 | 0 | 0 | 720 | 3 |
| Engineer | 200 | 50 | 0 | 20 | 0 | 600 | 1 |

### Unit Role Design

- **Soldier:** Baseline DPS, affordable, versatile. First unit you build.
- **Ranger:** Higher DPS, longer range, glass cannon. Good for kiting.
- **Heavy:** Tank, high armor reduces zombie melee damage significantly. Slow.
- **Sniper:** Extreme range, immobile. Best garrisoned in towers.
- **Engineer:** Support, can garrison for repairs (future), low combat value.

### DPS Efficiency Analysis

| Unit | Cost (Gold equiv) | DPS | DPS/100g | Verdict |
|------|-------------------|-----|----------|---------|
| Soldier | 120 | 12.0 | 10.0 | Efficient baseline |
| Ranger | 220 | 18.8 | 8.5 | Slightly less efficient, better range |
| Heavy | 480 | 20.0 | 4.2 | Tank, not for raw DPS |
| Sniper | 340 | 24.0 | 7.1 | Best with garrison bonus |
| Engineer | 270 | 8.0 | 3.0 | Utility, not combat |

---

## Zombie Balance

### Zombie Stats

| Zombie | HP | Damage | Cooldown | DPS | Speed | Range | Weight |
|--------|------|--------|----------|-----|-------|-------|--------|
| Walker | 60 | 6 | 1.2s | 5.0 | 50 | 1.0 | 100 |
| Runner | 35 | 4 | 0.8s | 5.0 | 160 | 1.0 | 50 |
| Fatty | 600 | 25 | 2.5s | 10.0 | 25 | 1.5 | 10 |
| Spitter | 50 | 18 | 2.0s | 9.0 | 60 | 5.0 | 20 |
| Doom | 2500 | 60 | 3.0s | 20.0 | 40 | 2.0 | 1 |

### Spawn Weight Distribution

Based on weights: Walker=100, Runner=50, Fatty=10, Spitter=20, Doom=1

| Zombie | Weight | % of Wave |
|--------|--------|-----------|
| Walker | 100 | 55.2% |
| Runner | 50 | 27.6% |
| Fatty | 10 | 5.5% |
| Spitter | 20 | 11.0% |
| Doom | 1 | 0.6% |

### Time-to-Kill Analysis (by Soldier, 12 DPS)

| Zombie | HP | TTK | Notes |
|--------|------|-----|-------|
| Walker | 60 | 5.0s | Standard target |
| Runner | 35 | 2.9s | Fast but fragile |
| Fatty | 600 | 50.0s | Needs focus fire |
| Spitter | 50 | 4.2s | Priority target (ranged) |
| Doom | 2500 | 208.0s | Boss, needs army |

---

## Tech Tree Balance

### Research Items

| Research | Tech Unlocked | Cost | Time (frames) | Prereq |
|----------|---------------|------|---------------|--------|
| AdvancedLogging | Tech 1 | 15g/10w | 120 | None |
| ImprovedMining | Tech 0 | 15g/15w/10s | 120 | None |
| EfficientSmelting | Tech 3 | 30g/20w/20s/10i | 180 | Tech 0 |
| OilRefining | Tech 2 | 50g/30s/20i | 180 | None |
| TradingPost | Tech 4 | 40g/30w | 180 | None |
| MasterCraftsmen | Tech 5 | 100g/60w/60s/40i/20o | 360 | Tech 3 |

### Tech Unlock Gates

| Tech | Unlocks Buildings | Unlocks Units | Gate Type |
|------|-------------------|---------------|-----------|
| 0 | Barracks | - | Entry (basic training) |
| 1 | Cottage | Engineer | Housing upgrade |
| 2 | OilRefinery | Heavy | Advanced resources + tank |
| 3 | SniperTower, AdvancedLab | Sniper | Long-range combat |
| 4 | Bank | - | Economy optimization |
| 5 | TeslaCoil | - | Endgame defense |

### Tech Bonuses

| Tech | Bonus |
|------|-------|
| ImprovedMining | +25% Stone, +15% Iron |
| AdvancedLogging | +30% Wood |
| EfficientSmelting | +25% Iron |
| OilRefining | +20% Oil |
| TradingPost | +20% Gold |
| MasterCraftsmen | +10% All resources |

---

## Zone Balance

### Zone Tiers

| Zone | Radius | Zombie Density | Allowed Types | Resource Density |
|------|--------|----------------|---------------|------------------|
| SafeZone | 0-25% | 0x | None | 0x |
| Tier1 | 25-45% | 0.5x | Walker | 0.5x |
| Tier2 | 45-70% | 1.0x | All except Doom | 1.0x |
| Tier3 | 70-100% | 1.5x | All (incl. Doom) | 1.5x |

### Zone Progression Intent

- **SafeZone (0-25%):** Protected starting area, no threats, no resources. Build economy here.
- **Tier1 (25-45%):** Light resistance (Walkers only), some resources. First expansion target.
- **Tier2 (45-70%):** Full zombie variety, good resources. Mid-game expansion.
- **Tier3 (70-100%):** Dangerous (Doom spawns), best resources. Late-game risk/reward.

---

## Balance Validation

### Wave Survival Requirements

For each wave, calculate:
- **Required DPS** = Total zombie HP / Engagement time (~60s)
- **Gold investment** = Required DPS / 0.10 DPS/gold (Soldier efficiency)

| Wave | HP | Required DPS | Gold Investment | Achievable? |
|------|------|--------------|-----------------|-------------|
| 1 (6m) | 2,000 | 33 | 330g | Yes (600g income) |
| 3 (18m) | 4,500 | 75 | 750g | Yes (1,800g income) |
| 5 (30m) | 8,000 | 133 | 1,330g | Yes (3,600g income) |
| 7 (42m) | 12,000 | 200 | 2,000g | Yes (5,000g income) |
| 10 (60m) | 45,000 | 750 | 7,500g | Yes (10,000g+ income) |

### Key Balance Checks

1. **First Soldier before Wave 1:**
   - 6 minutes × 5 g/s = 1,800g available
   - Barracks (250g) + Soldier (120g) = 370g → Affordable at ~75s
   - Player has ~5 min to train 4-5 soldiers

2. **Housing bottleneck at Wave 3-4:**
   - 4 pop from CC, +4 per Tent, +8 per Cottage
   - Wave 3 army (~8 units) needs 2 Tents or 1 Cottage
   - Forces player to invest in housing infrastructure

3. **Tech gates create meaningful choices:**
   - Tech 2 (Heavy) unlocks at Iron Mine → expansion required
   - Tech 4 (Bank) requires research time investment
   - Tech 5 (TeslaCoil) requires oil → outer zone expansion

4. **Final wave requires preparation:**
   - 45,000 HP needs ~750 DPS sustained
   - Turrets (100 DPS each) + Army (300-400 DPS) = ~10-15 turrets + full army
   - Forces investment in both mobile and static defense

---

## Recommended Changes Summary

### Critical Changes
1. `WaveConfig.json`: FramesPerDay 3600 → 7200
2. All unit/building build times: ~1.5x increase
3. All costs: ~1.2x increase (balanced for longer economy)
4. Zombie HP: Slight increase for slower combat feel

### Nice-to-Have
1. Zone safe radius: 0.30 → 0.25 (encourage earlier expansion)
2. Mini-wave frequency: 1800 → 3600 frames (less constant pressure)
3. Horde warning: 3600 → 7200 frames (more prep time)
