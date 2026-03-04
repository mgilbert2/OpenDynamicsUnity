# Pattern Interference Guide

## Problem: Sudden Recall Drop After Multiple Patterns

If you're experiencing a **sudden drop from 100% to 0% recall after training 5+ patterns**, this is likely due to **pattern interference**.

## What Causes Pattern Interference?

When multiple patterns overlap spatially, their learned attractors (wells) can interfere with each other:

1. **Overlapping Wells**: If patterns share similar positions, wells from different patterns stack up, creating conflicting gradients
2. **Gradient Cancellation**: Wells from different patterns can cancel each other out if they're in similar positions
3. **Landscape Saturation**: Too many wells create a "flat" or chaotic landscape where no clear path exists

## Symptoms

- ✅ First 1-4 patterns: 100% recall
- ❌ After 5+ patterns: Sudden drop to 0% recall
- ⚠️ Warning in console: "Weak learned gradient detected" or "Very weak learned gradient"

## Solutions

### Solution 1: Increase Well Width (Easiest)

**Location:** `LearningImprint` → `hypoWidth`

**Current:** `1.2f`  
**Recommended:** `1.5f` to `2.0f`

**Why:** Wider wells are more forgiving and less likely to interfere with each other. They create smoother gradients.

**Trade-off:** Wider wells may reduce precision for very similar patterns.

---

### Solution 2: Reduce Well Density

**Location:** `LearningImprint` → `sampleInterval`

**Current:** `0.05f` (20 samples/second)  
**Recommended:** `0.1f` to `0.15f` (10-6.7 samples/second)

**Why:** Fewer wells = less interference. Each pattern will create fewer attractors.

**Trade-off:** Less dense learning may reduce recall accuracy.

---

### Solution 3: Reduce Learning Rate

**Location:** `LearningImprint` → `learningRate`

**Current:** `0.2f`  
**Recommended:** `0.1f` to `0.15f`

**Why:** Weaker wells are less likely to interfere. Patterns will still be learned, but with gentler attractors.

**Trade-off:** Weaker learning may require more training passes.

---

### Solution 4: Use Pattern-Specific Learning (Advanced)

Train patterns in separate "sessions" and only test recall for patterns trained in the same session. This prevents interference between unrelated patterns.

**Implementation:** Modify `CSVExperimentRunner` to clear learning between pattern groups.

---

### Solution 5: Increase Landscape Gain

**Location:** `StatePointController` → `landscapeGain`

**Current:** `10f`  
**Recommended:** `15f` to `20f`

**Why:** Stronger response to the learned landscape can help overcome interference.

**Trade-off:** May make the ball too sensitive to attractors.

---

## Recommended Quick Fix

For immediate improvement, try this combination:

1. **Increase `hypoWidth`** from `1.2f` to `1.8f` (in `LearningImprint`)
2. **Increase `sampleInterval`** from `0.05f` to `0.1f` (in `LearningImprint`)
3. **Reduce `learningRate`** from `0.2f` to `0.15f` (in `LearningImprint`)

This reduces well density and makes wells more forgiving, reducing interference.

---

## Diagnostic Tools

The system now logs:
- **Well count** after each pattern training
- **Gradient magnitude** during recall tests
- **Warnings** if gradient is suspiciously weak

Check the Unity Console for these messages to diagnose interference issues.

---

## Understanding the Math

Each well creates a Gaussian potential:
```
V = -depth * exp(-r² / (2 * width²))
```

When many wells overlap:
- If they're in similar positions → gradients add up (good)
- If they're in conflicting positions → gradients cancel out (bad)
- If there are too many → landscape becomes flat (bad)

**Solution:** Make wells wider and less dense so they don't interfere as much.
