# Quarter Magnet Experiment: Parameter Tuning Guide

## Goal
**Learn patterns deep enough so that when the magnet cues for 25%, the ball continues rolling through the grooves at the right pace/momentum to finish patterns (or begin controlled wandering).**

---

## Parameter Relationships

### The Velocity Chain

```
Well Depth → Gradient Strength → Acceleration → Velocity → Momentum
     ↓              ↓                ↓            ↓          ↓
maxWellDepth   landscapeGain    externalGain  damping   maxSpeed
                                                      velocityMultiplier
```

**Key Insight**: You need to balance:
- **Strong enough gradients** to guide the ball (from deep wells + high landscapeGain)
- **Right velocity** to roll through without shooting out (controlled by damping, maxSpeed, velocityMultiplier)

---

## All Parameters Explained

### 1. **Well Depth Parameters** (Learning Strength)

#### `maxWellDepth` (0.0 - 3.0, recommended: 2.0 - 2.5)
**What it does**: Maximum depth any single well can reach. Deeper wells = stronger gradients.

**Effect on System**:
- **Too low** (< 1.5): Wells too shallow → weak gradients → ball doesn't follow pattern well
- **Too high** (> 3.0): Wells too deep → very strong gradients → ball gains too much velocity → shoots out
- **Just right** (2.0 - 2.5): Strong enough to guide, not so strong it overshoots

**Formula**: Gradient strength ∝ `wellDepth / (wellWidth²)`

**Recommendation**: Start with **2.5**, adjust down if ball shoots out, up if ball doesn't follow pattern.

---

#### `normalizeDepth` (true/false, recommended: false)
**What it does**: If enabled, scales all wells so the maximum equals `normalizedDepthTarget`.

**When to use**: Only if you want consistent well depths across all patterns. Usually not needed if `maxWellDepth` is set correctly.

---

### 2. **Gradient Strength Parameters** (How Strongly Wells Pull)

#### `landscapeGain` (1.0 - 30.0, recommended: 8.0 - 15.0)
**What it does**: Multiplies the gradient from wells. Higher = wells pull ball more strongly.

**Effect on System**:
- **Too low** (< 5.0): Ball doesn't follow wells well, wanders off path
- **Too high** (> 20.0): Ball gets pulled too strongly, gains excessive velocity
- **Just right** (8.0 - 15.0): Ball follows grooves nicely, maintains reasonable speed

**Formula**: `acceleration = landscapeGain × gradient + externalGain × magnetForce`

**Recommendation**: Start with **10.0**, increase if ball doesn't follow pattern, decrease if ball shoots out.

---

#### `externalGain` (0.0 - 200.0, **CRITICAL: must be > 55**, recommended: 60 - 90)
**What it does**: Multiplies the magnet force during the first 25% of recall.

**Effect on System**:
- **Too low** (< 55): **Magnet doesn't work effectively** - ball won't move properly
- **Too high** (> 100): Magnet gives ball excessive initial velocity → shoots out after 25%
- **Just right** (60 - 90): Ball gains good initial momentum, magnet works effectively

**CRITICAL**: This parameter must be **above 55** for the magnet to function properly. Values below 55 will prevent the ball from moving correctly.

**Recommendation**: Start with **60**, increase to 70-90 if you need more initial velocity, decrease if ball shoots out (but never go below 55).

---

### 3. **Velocity Control Parameters** (Momentum Management)

#### `ballDamping` (0.1 - 10.0, recommended: 0.5 - 1.5)
**What it does**: Friction coefficient. Lower = less friction = more momentum persists.

**Effect on System**:
- **Too low** (< 0.3): Velocity decays too slowly → ball maintains too much speed → shoots out
- **Too high** (> 2.0): Velocity decays too quickly → ball stops before finishing pattern
- **Just right** (0.5 - 1.5): Velocity decays gradually, ball maintains momentum through pattern

**Formula**: `velocity = velocity × (1 - damping × deltaTime)`

**Recommendation**: Start with **0.7**, decrease if ball stops early, increase if ball shoots out.

---

#### `ballMaxSpeed` (1.0 - 50.0, recommended: 8.0 - 12.0)
**What it does**: Maximum velocity cap. Prevents ball from exceeding this speed.

**Effect on System**:
- **Too low** (< 6.0): Ball artificially slowed, may not have enough momentum
- **Too high** (> 15.0): Ball can gain excessive speed from deep wells → shoots out
- **Just right** (8.0 - 12.0): Allows natural momentum while preventing runaway velocity

**Recommendation**: Start with **10.0**, decrease if ball shoots out, increase if ball stops early.

---

#### `ballVelocityMultiplier` (0.1 - 5.0, recommended: 0.8 - 1.2)
**What it does**: Fine-tune velocity each frame. Applied after damping but before maxSpeed cap.

**Effect on System**:
- **< 1.0**: Reduces velocity (e.g., 0.9 = 10% reduction)
- **= 1.0**: No change (normal)
- **> 1.0**: Boosts velocity (e.g., 1.1 = 10% boost)

**Use case**: Fine-tuning when other parameters are close but not quite right.

**Recommendation**: Start with **1.0**, adjust by ±0.1 for fine-tuning.

---

### 4. **Magnet Control Parameters**

#### `recallMagnetForceMultiplier` (-1.0 = default, or 0.0 - 5.0)
**What it does**: Multiplies magnet force during recall tests. -1.0 uses magnet's default value.

**Effect on System**:
- **Lower** (0.5 - 1.0): Gentler magnet pull, less initial velocity
- **Higher** (2.0 - 3.0): Stronger magnet pull, more initial velocity
- **0.0**: Magnet off (only used if `turnOffMagnetAtQuarterRecall` is true)

**Recommendation**: Use **-1.0** (default) unless you need to adjust magnet strength.

---

#### `turnOffMagnetAtQuarterRecall` (true/false, recommended: true)
**What it does**: Turns magnet off at 25% progress during recall.

**This is the core of the experiment**: Tests if ball can roll through remaining 75% using only learned wells.

---

### 5. **Initial Kick Parameters** (Usually Disabled)

#### `applyInitialKick` (true/false, recommended: false)
**What it does**: Applies velocity boost when magnet turns off.

**Why usually disabled**: Wells provide enough natural momentum. Kick often causes overshooting on later patterns.

**Recommendation**: **Keep disabled** (`false`) unless you have a specific reason.

---

## Recommended Starting Configuration

### For Pattern #1 (First Pattern)
```csharp
// Learning Parameters
maxWellDepth = 2.5f;
normalizeDepth = false;

// Gradient Strength
landscapeGain = 10.0f;
externalGain = 60.0f;  // CRITICAL: Must be > 55

// Velocity Control
ballDamping = 0.7f;
ballMaxSpeed = 10.0f;
ballVelocityMultiplier = 1.0f;

// Magnet Control
recallMagnetForceMultiplier = -1.0f;  // Use default
turnOffMagnetAtQuarterRecall = true;

// Initial Kick (DISABLED)
applyInitialKick = false;
```

### For Pattern #2+ (Later Patterns)
**Adjust if ball shoots out**:
```csharp
// Reduce well depth slightly
maxWellDepth = 2.0f;  // Down from 2.5

// Reduce gradient strength slightly
landscapeGain = 8.0f;  // Down from 10.0

// Increase damping slightly
ballDamping = 1.0f;   // Up from 0.7

// Lower max speed
ballMaxSpeed = 8.0f;   // Down from 10.0
```

---

## Tuning Strategy

### Step 1: Start with Recommended Values
Use the "Pattern #1" configuration above.

### Step 2: Test Pattern #1 Recall
**If ball shoots out**:
1. ✅ Lower `maxWellDepth` by 0.2-0.5
2. ✅ Lower `landscapeGain` by 1-2
3. ✅ Increase `ballDamping` by 0.2-0.3
4. ✅ Lower `ballMaxSpeed` by 1-2

**If ball stops early**:
1. ✅ Increase `maxWellDepth` by 0.2-0.5
2. ✅ Increase `landscapeGain` by 1-2
3. ✅ Decrease `ballDamping` by 0.2-0.3
4. ✅ Increase `ballMaxSpeed` by 1-2

### Step 3: Test Pattern #2+ Recall
**If pattern #1 works but #2+ shoots out**:
- This is normal! Later patterns have deeper wells from cumulative learning.
- Apply the "Pattern #2+" adjustments above.

### Step 4: Fine-Tune with `ballVelocityMultiplier`
If you're close but not quite right:
- **Too fast**: Set `ballVelocityMultiplier = 0.9` (10% reduction)
- **Too slow**: Set `ballVelocityMultiplier = 1.1` (10% boost)

---

## Understanding the Physics Flow

### During First 25% (Magnet On)
1. **Magnet pulls ball** → `acceleration = externalGain × magnetForce`
2. **Ball gains velocity** from magnet
3. **Wells also pull ball** → `acceleration += landscapeGain × wellGradient`
4. **Velocity builds up** → `velocity += acceleration × deltaTime`
5. **Damping reduces velocity** → `velocity -= velocity × damping × deltaTime`
6. **Max speed caps velocity** → `if (velocity > maxSpeed) velocity = maxSpeed`

### During Last 75% (Magnet Off)
1. **Only wells pull ball** → `acceleration = landscapeGain × wellGradient`
2. **Ball maintains momentum** from first 25%
3. **Velocity decays** from damping
4. **Wells guide direction** but also add velocity
5. **Balance**: Need enough gradient to guide, but not so much it overshoots

---

## Key Insights

### Why Pattern #2+ is Harder
- **Cumulative learning**: Wells from previous patterns are deeper
- **Stronger gradients**: Deeper wells → stronger gradients → more velocity
- **Solution**: Slightly lower `maxWellDepth` and `landscapeGain` for later patterns

### The Sweet Spot
You want:
- **Deep enough wells** (2.0 - 2.5) to create strong gradients
- **Moderate landscapeGain** (8.0 - 12.0) to pull ball but not too hard
- **Low damping** (0.5 - 1.0) to maintain momentum
- **Moderate max speed** (8.0 - 12.0) to allow momentum but prevent overshooting

### The Trade-off
- **Too much gradient strength**: Ball gains too much velocity → shoots out
- **Too little gradient strength**: Ball doesn't follow pattern → wanders off
- **Balance**: Strong enough to guide, gentle enough to maintain control

---

## Quick Reference: Parameter Effects

| Parameter | Increase If... | Decrease If... |
|-----------|---------------|----------------|
| `maxWellDepth` | Ball doesn't follow pattern | Ball shoots out |
| `landscapeGain` | Ball wanders off path | Ball gains too much velocity |
| `externalGain` | Need more initial velocity (but keep > 55) | Ball shoots out after 25% (but never go below 55) |
| `ballDamping` | Ball shoots out | Ball stops early |
| `ballMaxSpeed` | Ball stops early | Ball shoots out |
| `ballVelocityMultiplier` | Need fine boost | Need fine reduction |

---

## Example: Perfect Balance

**Goal**: Ball follows pattern smoothly, maintains momentum, doesn't shoot out.

**Configuration**:
```csharp
maxWellDepth = 2.3f;           // Deep enough to guide
landscapeGain = 10.0f;          // Strong enough to pull
externalGain = 60.0f;            // CRITICAL: Must be > 55 for magnet to work
ballDamping = 0.7f;              // Maintains momentum
ballMaxSpeed = 10.0f;            // Prevents overshooting
ballVelocityMultiplier = 1.0f;  // No fine-tuning needed
```

**Result**:
- First 25%: Magnet pulls ball, gains moderate velocity
- Last 75%: Ball rolls through wells, guided by gradients, maintains momentum
- Success! ✅

---

## Troubleshooting

### Ball shoots out immediately after 25%
- ✅ Lower `maxWellDepth` (try 2.0)
- ✅ Lower `landscapeGain` (try 8.0)
- ✅ Increase `ballDamping` (try 1.0)
- ✅ Lower `ballMaxSpeed` (try 8.0)

### Ball stops before finishing pattern
- ✅ Increase `maxWellDepth` (try 2.5)
- ✅ Increase `landscapeGain` (try 12.0)
- ✅ Decrease `ballDamping` (try 0.5)
- ✅ Increase `ballMaxSpeed` (try 12.0)

### Ball follows pattern but too slowly
- ✅ Increase `landscapeGain` slightly (try 11.0)
- ✅ Decrease `ballDamping` slightly (try 0.6)
- ✅ Use `ballVelocityMultiplier = 1.1` for fine boost

### Ball follows pattern but too fast
- ✅ Decrease `landscapeGain` slightly (try 9.0)
- ✅ Increase `ballDamping` slightly (try 0.8)
- ✅ Use `ballVelocityMultiplier = 0.9` for fine reduction

---

## Summary

**The key to success**: Balance well depth (learning strength) with velocity control (momentum management).

- **Deep wells** (2.0 - 2.5) + **moderate landscapeGain** (8.0 - 12.0) = strong guidance
- **Low damping** (0.5 - 1.0) + **moderate max speed** (8.0 - 12.0) = maintained momentum
- **Fine-tune** with `ballVelocityMultiplier` if close but not quite right

Start with recommended values, test, and adjust based on behavior!
