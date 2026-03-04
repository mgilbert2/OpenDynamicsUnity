# Recall Rate Optimization Guide

## Problem: Low Recall Rates

If you're getting low recall rates and want **100% recall for at least 4 patterns**, follow this guide to optimize your experiment parameters.

## Understanding Pattern Size

**Your patterns are approximately 12 units across** (ranging from about -6 to +6 in both X and Z).

**Why this matters:** The recall radius threshold should be proportional to pattern size:
- **Small patterns** (2-4 units): Use `recallRadiusThreshold = 0.5f` to `1.0f`
- **Medium patterns** (4-8 units): Use `recallRadiusThreshold = 1.5f` to `2.0f`
- **Large patterns** (8-15 units): Use `recallRadiusThreshold = 2.0f` to `3.0f` ✅ **Your case**
- **Very large patterns** (15+ units): Use `recallRadiusThreshold = 3.0f` to `4.0f`

**Rule of thumb:** Recall radius should be about **15-25% of pattern diameter** for reasonable recall, or **20-30%** for easier recall.

For your ~12 unit patterns:
- `2.0f` = ~17% of diameter (moderate difficulty)
- `2.5f` = ~21% of diameter (recommended) ✅
- `3.0f` = ~25% of diameter (easier) ✅

## Key Parameters That Affect Recall

### 1. **Recall Radius Threshold** (Most Important!)
**Location:** `CSVExperimentRunner` → Experiment Config → `recallRadiusThreshold`

**Current Default:** `2.0f` units  
**Recommended for 100% recall (12-unit patterns):** `2.5f` to `3.0f` units

**What it does:** This is the distance the ball must stay within from the magnet to count as "in range". If this is too small relative to pattern size, even good recall will fail.

**How to adjust (based on pattern size):**
- **For your ~12 unit patterns:** Start with `2.5f` (21% of diameter)
- If still failing, try `3.0f` (25% of diameter)
- For smaller patterns (6-8 units): Use `1.5f` to `2.0f`
- For larger patterns (15+ units): Use `3.0f` to `4.0f`
- Too large (>30% of pattern diameter) and the test becomes meaningless

---

### 2. **Training Passes Per Pattern** (NEW!)
**Location:** `CSVExperimentRunner` → Experiment Config → `trainingPassesPerPattern`

**Current Default:** `1`  
**Recommended for 100% recall:** `2` to `3`

**What it does:** Trains each pattern multiple times before recall test. More passes = stronger learning.

**How to adjust:**
- Set to `2` for moderate improvement
- Set to `3` for maximum learning strength
- More than 3 may cause overfitting

---

### 3. **Noise Strength During Recall**
**Location:** `CSVExperimentRunner` → Experiment Config → `noiseStrength`

**Current Default:** `2.0f`  
**Recommended for 100% recall:** `1.0f` to `1.5f`

**What it does:** Adds random noise to ball movement during recall test. Lower noise = easier recall.

**How to adjust:**
- Start with `1.5f` (moderate noise)
- For easier recall, try `1.0f` (low noise)
- For harder recall, try `2.5f` (high noise)

---

### 4. **Learning Parameters** (In LearningImprint Component)
**Location:** Unity Inspector → `LearningImprint` component

#### **Learning Rate**
- **Current Default:** `0.2f`
- **Recommended for 100% recall:** `0.3f` to `0.5f`
- **What it does:** How much is learned per sample. Higher = faster/stronger learning.

#### **Hypo Depth** (Attractor Strength)
- **Current Default:** `10f`
- **Recommended for 100% recall:** `15f` to `20f`
- **What it does:** Strength of learned attractors. Higher = stronger pull toward learned path.

#### **Hypo Width** (Attractor Spread)
- **Current Default:** `1.2f`
- **Recommended for 100% recall:** `1.5f` to `2.0f`
- **What it does:** Width of learned attractors. Wider = more forgiving, easier to stay in range.

---

### 5. **Magnet Force During Recall**
**Location:** `CSVExperimentRunner` → Experiment Config → `recallMagnetForceMultiplier`

**Current Default:** `-1f` (uses magnet's default force)  
**Recommended for 100% recall:** `1.2f` to `1.5f`

**What it does:** Increases magnet force during recall to help ball stay on track.

**How to adjust:**
- Set to `1.2f` for 20% stronger magnet force
- Set to `1.5f` for 50% stronger magnet force
- Higher values make recall easier but less realistic

---

### 6. **Ball Dynamics** (In StatePointController Component)
**Location:** Unity Inspector → `StatePointController` component

#### **Landscape Gain**
- **Current Default:** `10f`
- **Recommended for 100% recall:** `12f` to `15f`
- **What it does:** How strongly the ball follows the learned landscape. Higher = better recall.

#### **External Gain** (Magnet Force Weight)
- **Current Default:** `1.8f`
- **Recommended for 100% recall:** `2.0f` to `2.5f`
- **What it does:** How strongly the ball follows the magnet. Higher = better tracking.

---

## Quick Optimization Checklist

To achieve **100% recall for at least 4 patterns**, try these settings in order:

### Step 1: Easy Wins (Start Here)
1. ✅ Increase `recallRadiusThreshold` to `2.0f` or `2.5f`
2. ✅ Set `trainingPassesPerPattern` to `2`
3. ✅ Reduce `noiseStrength` to `1.5f`

### Step 2: Learning Parameters
4. ✅ Increase `LearningImprint.learningRate` to `0.3f`
5. ✅ Increase `LearningImprint.hypoDepth` to `15f`
6. ✅ Increase `LearningImprint.hypoWidth` to `1.5f`

### Step 3: Force & Dynamics
7. ✅ Set `recallMagnetForceMultiplier` to `1.2f`
8. ✅ Increase `StatePointController.landscapeGain` to `12f`
9. ✅ Increase `StatePointController.externalGain` to `2.0f`

### Step 4: If Still Failing
10. ✅ Increase `recallRadiusThreshold` to `3.0f`
11. ✅ Set `trainingPassesPerPattern` to `3`
12. ✅ Reduce `noiseStrength` to `1.0f`

---

## Example Optimized Configuration

Here's a complete example configuration that should give you 100% recall for at least 4 patterns:

**Note:** This configuration assumes patterns are ~12 units in diameter (like your `geo_XX` patterns).

### CSVExperimentRunner Config:
```
recallRadiusThreshold = 2.5f  (21% of 12-unit pattern diameter)
trainingPassesPerPattern = 2
noiseStrength = 1.2f
recallMagnetForceMultiplier = 1.2f
recallRequiredPercent = 80f
```

### LearningImprint Component:
```
learningRate = 0.3f
hypoDepth = 18f
hypoWidth = 1.6f
sampleInterval = 0.05f
```

### StatePointController Component:
```
landscapeGain = 13f
externalGain = 2.2f
damping = 4f
maxSpeed = 12f
```

---

## Understanding Recall Test Logic

The recall test works like this:

1. **Training Phase:** Ball follows magnet path with learning ON. Creates attractors along the path.
2. **Recall Phase:** Learning OFF, noise ON. Ball must follow the same path using only learned attractors.
3. **Measurement:** Every `recallSampleInterval` seconds, check if ball is within `recallRadiusThreshold` of magnet.
4. **Pass/Fail:** If `recallPercentInRange >= recallRequiredPercent`, test passes.

**Key Insight:** The ball needs strong, wide attractors (from learning) to overcome noise during recall.

---

## Troubleshooting

### Problem: All patterns failing recall
**Solution:** 
- Increase `recallRadiusThreshold` to `2.5f` or `3.0f`
- Increase `trainingPassesPerPattern` to `3`
- Reduce `noiseStrength` to `1.0f`

### Problem: Some patterns pass, others fail
**Solution:**
- Patterns with sharp turns or tight curves are harder
- Try increasing `hypoWidth` to `2.0f` for more forgiving attractors
- Increase `trainingPassesPerPattern` to `3` for difficult patterns

### Problem: Ball not learning at all
**Solution:**
- Check that `LearningImprint.statePoint` is assigned to the ball's transform
- Verify `learningOn` is true during training (check Unity console logs)
- Ensure `LearningImprint.learningRate > 0`

### Problem: Ball moves but doesn't follow learned path
**Solution:**
- Increase `landscapeGain` to `15f`
- Increase `hypoDepth` to `20f`
- Check that `AttractorField` is using `LearningImprint` as a source

---

## Testing Strategy

1. **Start with 4 simple patterns** (e.g., `geo_01`, `geo_02`, `geo_03`, `geo_04`)
2. **Use optimized settings** from Step 1-2 above
3. **Run experiment** and check recall rates
4. **If all 4 pass:** Great! Try more patterns or harder ones
5. **If some fail:** Apply Step 3-4 optimizations
6. **Iterate** until you get 100% recall for at least 4 patterns

---

## Notes

- **Pattern size matters!** Always set `recallRadiusThreshold` relative to your pattern diameter (15-25% is recommended)
- **Recall radius threshold** is the easiest parameter to adjust and has the biggest impact
- **Multiple training passes** significantly improve learning strength
- **Lower noise** makes recall easier but less realistic
- **Wider attractors** (`hypoWidth`) are more forgiving for complex patterns
- **Stronger learning** (`hypoDepth`, `learningRate`) creates more stable recall

## How to Check Your Pattern Size

To determine your pattern size:
1. Open your CSV file in Excel or a text editor
2. Find the min/max X and Z values for a pattern
3. Calculate: `pattern_diameter = max(max_x - min_x, max_z - min_z)`
4. Set `recallRadiusThreshold = pattern_diameter * 0.20` to `pattern_diameter * 0.25`

**Example:** If your pattern ranges from -6 to +6 in both axes:
- Diameter = 12 units
- Recommended threshold = 12 × 0.21 = **2.5f** ✅

Good luck! 🎯
