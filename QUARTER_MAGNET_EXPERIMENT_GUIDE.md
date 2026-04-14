# Quarter Magnet Experiment Guide

## Experiment Overview

The "quarter magnet" experiment tests whether learned attractor wells can guide the ball through patterns **without** the magnet's help. The magnet pulls the ball for the first 25% of the pattern, then turns off, allowing the ball to roll through the remaining 75% using only momentum and learned wells.

## Key Parameters & Recommendations

### Critical Parameters

#### 1. **Well Depth Control** (Prevents Stuck Ball & Catastrophic Forgetting)
- **`maxWellDepth`**: **2.0 - 3.0** (CRITICAL: Never exceed 3.0)
  - Prevents wells from becoming too deep
  - Stops ball from getting stuck
  - Prevents landscape flattening during catastrophic forgetting
- **`normalizeDepth`**: **false** (recommended) or **true** with `normalizedDepthTarget = 1.0`
  - If enabled, keeps all wells proportional but may flatten landscape
  - Hard cap (`maxWellDepth`) is usually sufficient

#### 2. **Ball Physics** (Controls Momentum & Rolling)
- **`ballDamping`**: **0.5 - 2.0** (lower = more momentum, less friction)
  - **0.07** (your current): Very low friction, ball maintains velocity well
  - **0.5 - 1.0**: Good balance for rolling through patterns
  - **Warning**: Values < 0.5 can cause instability
- **`ballMaxSpeed`**: **8 - 15** (limits maximum velocity)
  - Lower values (8-10) prevent overshooting
  - Higher values (12-15) allow more momentum
  - Your current: **12** is reasonable

#### 3. **Magnet Control** (Core Experiment Feature)
- **`turnOffMagnetAtQuarterRecall`**: **true** (enables the experiment)
  - Magnet turns off at 25% pattern progress
  - Ball must rely on wells + momentum for remaining 75%

#### 4. **Initial Kick** (Optional Momentum Boost)
- **`applyInitialKick`**: **false** (RECOMMENDED) or **true** with very low speed
- **`initialKickSpeed`**: **0** (RECOMMENDED) or **0.001 - 0.01** if enabled
  - **Current behavior**: Kick only applies on first pattern if ball is truly stopped
  - **Problem**: Even tiny kicks can cause ball to shoot out on later patterns
  - **Solution**: **Disable entirely** (`applyInitialKick = false`) and rely on wells

## What We Learned

### Problem: Ball Shooting Out of Wells

**Root Cause**: After the first pattern, deeper wells give the ball significant velocity. When the magnet turns off at 25%, the ball is already moving fast. Adding even a tiny kick pushes it over the edge.

**Solutions Attempted**:
1. ✅ Reduced kick multipliers (50% → 10% → 5% → 0.5%)
2. ✅ Skip kick if ball has velocity (> 0.3 → > 0.1 → > 0.05)
3. ✅ Skip kick after first pattern (`currentLearningStage > 1`)
4. ✅ Lower speed caps (70% → 50% → 40% → 30% → 15%)
5. ✅ Bounds checking (skip kick near edges)

**Final Solution**: **Disable kick entirely** (`applyInitialKick = false`). The wells provide enough momentum naturally.

### Problem: Catastrophic Forgetting

**Root Cause**: Wells getting too deep → ball gets stuck → continuous learning deepens wells → landscape flattens.

**Solution**: Hard cap on well depth (`maxWellDepth = 3.0`) prevents any well from exceeding the limit, stopping learning at that location.

### Problem: Weak Gradient Warning

**Root Cause**: With many wells (200+), gradients can cancel out or become very small at certain locations.

**Solution**: Warning threshold adjusted to only trigger on truly weak gradients (< 0.01) with many wells (> 50).

## Recommended Configuration

```csharp
// Learning Parameters
maxWellDepth = 2.5f;              // Prevents stuck ball
normalizeDepth = false;            // Hard cap is sufficient
normalizedDepthTarget = 1.0f;      // Not used if normalizeDepth = false

// Ball Physics
ballDamping = 0.5f - 1.0f;        // Low friction for momentum
ballMaxSpeed = 10f;                // Prevents overshooting

// Magnet Control
turnOffMagnetAtQuarterRecall = true;  // Core experiment feature

// Initial Kick (DISABLED - wells provide enough momentum)
applyInitialKick = false;          // RECOMMENDED: Disable entirely
initialKickSpeed = 0f;             // Not used if disabled
```

## Experiment Flow

1. **Training Phase**: 
   - Magnet pulls ball through pattern (learning enabled)
   - Wells are carved at ball's path
   - Depth capped at `maxWellDepth`

2. **Recall Phase**:
   - Magnet pulls ball for first 25% of pattern
   - Magnet turns off at 25% progress
   - Ball rolls through remaining 75% using:
     - Momentum from first 25%
     - Gradient from learned wells
     - Low damping allows momentum to persist

3. **Success Criteria**:
   - Ball follows pattern path (within `recallRadiusThreshold`)
   - Ball maintains velocity through wells
   - No shooting out or getting stuck

## Troubleshooting

### Ball shoots out of wells:
- ✅ **Disable kick**: `applyInitialKick = false`
- ✅ **Lower `ballMaxSpeed`**: Try 8-10
- ✅ **Check well depth**: Ensure `maxWellDepth ≤ 3.0`
- ✅ **Increase damping slightly**: Try 1.0-2.0 if too fast

### Ball gets stuck:
- ✅ **Lower `maxWellDepth`**: Try 2.0-2.5
- ✅ **Enable normalization**: `normalizeDepth = true`, `normalizedDepthTarget = 1.0`
- ✅ **Check for too many wells**: Consider `wellMergeDistance` to merge nearby wells

### Ball doesn't roll far enough:
- ✅ **Lower damping**: Try 0.5-1.0
- ✅ **Increase `ballMaxSpeed`**: Try 12-15
- ✅ **Check well depth**: Ensure wells are deep enough (but not too deep!)

### Weak gradient warnings:
- Normal with many wells - gradients can cancel out
- Only concerning if ball completely stops following pattern
- Consider reducing number of patterns or increasing `wellMergeDistance`

## Final Fix Applied

The kick function now:
1. ✅ Checks if ball has ANY velocity (> 0.05) → skip kick
2. ✅ Only applies on first pattern (`currentLearningStage ≤ 1`)
3. ✅ Uses minimal multiplier (0.5% of config) if it does apply
4. ✅ **RECOMMENDATION**: Set `applyInitialKick = false` to disable entirely

## Key Takeaways

1. **Wells provide natural momentum** - No kick needed after first pattern
2. **Hard cap is essential** - Prevents stuck ball and catastrophic forgetting
3. **Low damping is key** - Allows ball to maintain momentum through wells
4. **First pattern is special** - May need tiny kick, but later patterns don't
5. **Balance is critical** - Too much momentum = shooting out, too little = getting stuck
