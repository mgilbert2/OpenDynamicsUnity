# Quarter Magnet Experiment: Velocity & Momentum Guide

## Overview

The quarter magnet experiment tests whether learned attractor wells can guide the ball through patterns **without** the magnet's help. The magnet pulls the ball for the first 25% of the pattern, then turns off, allowing the ball to roll through the remaining 75% using only momentum and learned wells.

**Critical Challenge**: Balancing momentum so the ball rolls through patterns without shooting out of wells.

---

## Key Velocity Parameters

### 1. **Ball Damping** (`ballDamping`)
**What it does**: Controls friction/drag on the ball. Lower = less friction = more momentum.

**Range**: `0.0 - 10.0` (Unity units)
- **0.0**: No friction (WARNING: Can cause instability or prevent movement)
- **0.5 - 1.0**: **RECOMMENDED** - Low friction, good momentum
- **2.0 - 4.0**: Moderate friction, more control
- **4.0+**: High friction, ball slows quickly

**Your Current**: `0.07` (Very low - may cause instability)

**Recommendation**: 
- **Start with `0.5 - 1.0`** for good momentum
- **If ball shoots out**: Increase to `1.0 - 2.0`
- **If ball stops too early**: Decrease to `0.3 - 0.5` (but watch for instability)

**Physics**: 
- Applied as: `velocity -= velocity * damping * deltaTime`
- Lower damping = velocity decays slower = ball maintains speed longer

---

### 2. **Ball Max Speed** (`ballMaxSpeed`)
**What it does**: Hard cap on maximum velocity magnitude. Prevents ball from moving too fast.

**Range**: `1.0 - 50.0` (world units/second)
- **5 - 8**: Very slow, controlled movement
- **8 - 12**: **RECOMMENDED** - Good balance
- **12 - 15**: Fast, more momentum
- **15+**: Very fast, high risk of overshooting

**Your Current**: `12.0` (Reasonable)

**Recommendation**:
- **If ball shoots out**: Lower to `8 - 10`
- **If ball doesn't roll far enough**: Increase to `12 - 15`
- **For first pattern**: Can be higher (12-15)
- **For later patterns**: Lower (8-10) to prevent overshooting

**Physics**:
- Applied as: `if (velocity.magnitude > maxSpeed) velocity = velocity.normalized * maxSpeed`
- Acts as a safety valve to prevent runaway velocity

---

### 3. **Initial Kick Speed** (`initialKickSpeed`)
**What it does**: Velocity boost applied when magnet turns off (if enabled).

**Range**: `0.0 - 50.0` (world units/second)
- **0.0**: **RECOMMENDED** - No kick, rely on wells
- **0.001 - 0.01**: Very tiny kick (usually unnecessary)
- **0.01 - 0.1**: Small kick (risky on later patterns)
- **0.1+**: Larger kick (high risk of shooting out)

**Your Current**: `0.001` (Very low, but still causing issues)

**Recommendation**: 
- **DISABLE ENTIRELY**: Set `applyInitialKick = false`
- **Why**: Wells provide enough natural momentum after first pattern
- **If you must enable**: Use `0.001` or lower, but expect issues on pattern #2+

**Current Behavior**:
- Only applies if ball speed < 0.05 AND it's the first pattern
- Uses 0.5% of configured value (so 0.001 becomes 0.000005)
- Still causes problems on later patterns

---

## Velocity Flow Through Experiment

### Pattern #1 (First Pattern)
1. **Training Phase**:
   - Magnet pulls ball → ball gains velocity from magnet force
   - Wells are carved at ball's path
   - Ball velocity: Moderate (from magnet)

2. **Recall Phase**:
   - **0-25%**: Magnet pulls ball → velocity builds up
   - **25%**: Magnet turns off → velocity maintained from first 25%
   - **25-100%**: Ball rolls through wells using:
     - **Momentum from first 25%** (main velocity source)
     - **Gradient from wells** (guides direction)
     - **Low damping** (maintains speed)
   - **Result**: Usually works well (shallow wells, moderate velocity)

### Pattern #2+ (Later Patterns)
1. **Training Phase**:
   - Magnet pulls ball → ball gains velocity
   - **Deeper wells** are carved (from previous patterns)
   - Ball velocity: Higher (from magnet + deeper wells)

2. **Recall Phase**:
   - **0-25%**: Magnet pulls ball → velocity builds up
   - **Ball rolls through deeper wells** → gains MORE velocity from gradients
   - **25%**: Magnet turns off → ball already moving FAST
   - **25-100%**: Ball tries to roll through wells but:
     - **Too much velocity** → shoots out of wells
     - **Wells are deeper** → stronger gradients → more velocity
     - **Low damping** → velocity doesn't decay fast enough
   - **Result**: Ball shoots out (too much momentum)

---

## Velocity Sources

### 1. **Magnet Force** (First 25%)
- **Strength**: Controlled by `recallMagnetForceMultiplier`
- **Effect**: Pulls ball forward, builds velocity
- **Duration**: First 25% of pattern
- **Result**: Ball has moderate-high velocity when magnet turns off

### 2. **Well Gradients** (All 100%)
- **Strength**: Proportional to well depth and proximity
- **Effect**: Pulls ball toward well centers
- **Duration**: Entire pattern
- **Result**: 
  - **Pattern #1**: Moderate gradients (shallow wells)
  - **Pattern #2+**: Strong gradients (deeper wells) → MORE velocity

### 3. **Initial Kick** (Optional, at 25%)
- **Strength**: `initialKickSpeed * 0.005` (if enabled)
- **Effect**: Adds velocity boost
- **Duration**: Instantaneous
- **Result**: Usually causes overshooting on later patterns

### 4. **Damping** (All 100%)
- **Strength**: `ballDamping`
- **Effect**: Reduces velocity over time
- **Duration**: Continuous
- **Result**: Lower damping = velocity decays slower = more momentum

---

## Velocity Balance Equation

**Total Velocity** = 
- Magnet velocity (first 25%) +
- Well gradient velocity (all 100%) +
- Initial kick velocity (at 25%, if enabled) -
- Damping losses (all 100%)

**For Success**:
- Velocity must be **high enough** to roll through pattern
- Velocity must be **low enough** to stay in wells
- Balance depends on well depth, damping, and max speed

---

## Parameter Recommendations by Scenario

### Scenario 1: Ball Shoots Out (Too Much Velocity)
**Symptoms**: Ball flies out of wells, especially on pattern #2+

**Solutions** (in order of effectiveness):
1. ✅ **Disable kick**: `applyInitialKick = false`
2. ✅ **Lower max speed**: `ballMaxSpeed = 8 - 10`
3. ✅ **Increase damping**: `ballDamping = 1.0 - 2.0`
4. ✅ **Lower well depth**: `maxWellDepth = 2.0 - 2.5` (reduces gradient strength)

**Recommended Settings**:
```
ballDamping = 1.0
ballMaxSpeed = 8.0
applyInitialKick = false
maxWellDepth = 2.5
```

---

### Scenario 2: Ball Stops Too Early (Too Little Velocity)
**Symptoms**: Ball doesn't roll far enough, stops before completing pattern

**Solutions**:
1. ✅ **Lower damping**: `ballDamping = 0.5 - 0.8`
2. ✅ **Increase max speed**: `ballMaxSpeed = 12 - 15`
3. ✅ **Increase well depth**: `maxWellDepth = 2.5 - 3.0` (stronger gradients)
4. ⚠️ **Enable tiny kick**: `applyInitialKick = true`, `initialKickSpeed = 0.001` (risky)

**Recommended Settings**:
```
ballDamping = 0.5
ballMaxSpeed = 12.0
applyInitialKick = false  (try without first)
maxWellDepth = 2.5
```

---

### Scenario 3: Pattern #1 Works, Pattern #2+ Fails
**Symptoms**: First pattern perfect, later patterns shoot out

**Root Cause**: Deeper wells on later patterns → stronger gradients → more velocity

**Solutions**:
1. ✅ **Disable kick entirely**: `applyInitialKick = false` (kick only helps pattern #1)
2. ✅ **Lower max speed for later patterns**: `ballMaxSpeed = 8 - 10`
3. ✅ **Increase damping slightly**: `ballDamping = 0.8 - 1.0`
4. ✅ **Lower well depth cap**: `maxWellDepth = 2.0 - 2.5`

**Recommended Settings**:
```
ballDamping = 0.8
ballMaxSpeed = 9.0
applyInitialKick = false
maxWellDepth = 2.0
```

---

## Velocity Debugging

### Check Ball Velocity
Add this to your code to monitor velocity:
```csharp
float currentSpeed = ball.GetSpeed();
Vector3 currentVel = ball.GetVelocityXZ();
Debug.Log($"Ball speed: {currentSpeed:F2}, velocity: {currentVel}");
```

### When to Check
- **At 25% progress** (when magnet turns off): Should be moderate (2-5 speed units)
- **At 50% progress**: Should be maintained or slightly decreased
- **At 75% progress**: Should still be moving (1-3 speed units)
- **If ball shoots out**: Check velocity just before shooting (likely > 5-6 speed units)

### Velocity Thresholds
- **< 0.5**: Ball is stopped or nearly stopped
- **0.5 - 2.0**: Slow, controlled movement
- **2.0 - 5.0**: **GOOD** - Moderate speed, should stay in wells
- **5.0 - 8.0**: Fast, risk of overshooting
- **> 8.0**: Too fast, will likely shoot out

---

## Optimal Configuration

### For Quarter Magnet Experiment:
```
// Ball Physics
ballDamping = 0.8f;           // Low friction, maintains momentum
ballMaxSpeed = 9.0f;          // Prevents overshooting

// Learning
maxWellDepth = 2.5f;          // Prevents stuck ball, reduces gradient strength

// Magnet Control
turnOffMagnetAtQuarterRecall = true;

// Initial Kick (DISABLED)
applyInitialKick = false;     // Wells provide enough momentum
initialKickSpeed = 0f;        // Not used
```

### Why These Values?
- **Damping 0.8**: Low enough to maintain momentum, high enough to prevent runaway
- **Max Speed 9.0**: Limits velocity to prevent shooting out, but allows rolling
- **No Kick**: Wells provide natural momentum, kick causes problems
- **Well Depth 2.5**: Deep enough to guide ball, shallow enough to prevent excessive gradients

---

## Key Takeaways

1. **Velocity accumulates**: Magnet + wells + kick (if enabled) all add velocity
2. **Later patterns = more velocity**: Deeper wells → stronger gradients → more speed
3. **Damping is your friend**: Controls how fast velocity decays
4. **Max speed is a safety valve**: Prevents runaway velocity
5. **Kick is usually harmful**: Wells provide enough momentum naturally
6. **Balance is critical**: Too much = shooting out, too little = stopping early

---

## Testing Strategy

1. **Start conservative**: Low max speed (8), moderate damping (1.0), no kick
2. **Test pattern #1**: Should work well
3. **Test pattern #2**: If shoots out, lower max speed or increase damping
4. **Iterate**: Adjust parameters until both patterns work
5. **Scale up**: Test with more patterns (3, 4, 5+) to ensure stability

---

## Common Mistakes

1. ❌ **Too low damping** (< 0.3): Velocity never decays → shooting out
2. ❌ **Too high max speed** (> 15): No safety limit → shooting out
3. ❌ **Enabling kick**: Adds unnecessary velocity → shooting out
4. ❌ **Too deep wells** (> 3.0): Strong gradients → excessive velocity
5. ❌ **Not testing pattern #2+**: Pattern #1 works, but later patterns fail

---

## Final Recommendation

**For reliable quarter magnet experiments**:

```
ballDamping = 0.8f
ballMaxSpeed = 9.0f
applyInitialKick = false
maxWellDepth = 2.5f
enableCueFadeSystem = true
cueOffAtProgress = 0.25f   // or 0.5f for half-cue recall
```

**Monitor velocity at 25%, 50%, 75% progress** to ensure it stays in the 2-5 speed unit range. If it exceeds 6-7, lower max speed or increase damping.

---

## Partial Cue + Smooth Roll-Through (Halfway and Beyond)

**Goal**: Magnet acts as a **partial cue** (e.g. first half of the pattern), then turns off; the ball has enough velocity and low enough friction to **roll through the rest** on momentum and learned grooves, with **smooth patterns** so the path doesn’t feel jagged.

### 1. **Config: where the magnet turns off**

- **`cueOffAtProgress`** (0–1): When the magnet starts fading off.
  - **0.25** = quarter cue (magnet off after first 25%).
  - **0.5** = half cue (magnet off after first 50%) — good for “partial cue, then recall.”
  - **0.5–0.6** = ball gets a strong initial pull, then recall trail takes over.

Set this in the experiment config (Cue Fade System). The magnet stays at full strength until this progress, then smoothly fades to 0 by the end of the pattern.

### 2. **Enough velocity to roll through the rest**

- **Lower friction**: Use **`ballDamping`** in the **0.5–1.0** range so the ball keeps momentum after the magnet fades (e.g. **0.6–0.8** for “rolls through most of the rest”).
- **Reasonable cap**: **`ballMaxSpeed`** ~8–10 so the ball doesn’t shoot out of wells.
- **No extra kick**: Keep **`applyInitialKick = false`** so you’re not adding extra speed when the cue turns off.

Result: the ball gains speed during the cued segment, then coasts through the rest with wells guiding direction.

### 3. **Smooth learned patterns (grooves)**

So the ball follows smooth grooves instead of a bumpy path:

- **`hypoWidth`** (Learning Parameters): Width of each well. **Larger = smoother gradients** (e.g. **1.2–1.5**); **smaller = tighter wells** (e.g. 0.5 is already quite narrow—avoid going much below ~0.3). Applied to LearningImprint via config.
- **`wellMergeDistance`**: Merge nearby wells (e.g. **0.2–0.4** of hypoWidth) so the learned path is a smooth valley instead of many sharp wells.
- **`maxWellDepth`**: Keep moderate (e.g. **2.0–2.5**) so wells guide without overly sharp pulls.

Smooth learning + low friction + partial cue gives: **pattern learned smoothly → recall trail starts when magnet turns off (~halfway) → ball rolls through the rest of the grooves on momentum**.
