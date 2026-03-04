# Cumulative Recall Troubleshooting Guide

## Problem: Catastrophic Recall Failure (10-17% when 66.6% required)

When using **cumulative recall mode** with 6 patterns, you're experiencing very low recall rates despite the ball visually following the paths.

## Root Causes

### 1. **Pattern Interference (Most Likely)**
With 6 patterns learned cumulatively, **all wells from all patterns are active simultaneously**. This creates:
- **Thousands of overlapping attractors** competing with each other
- **Gradient cancellation** when patterns overlap spatially
- **Landscape saturation** where no clear path exists

**Check Unity Console for:**
- Well count after each pattern (should be logged)
- "Weak learned gradient detected" warnings
- If well count > 5000, you have severe interference

### 2. **Recall Radius Threshold Too Strict**
Your threshold is **3.0 units**, but with noise and interference, the ball might be following the path but consistently staying 3.1-4.0 units away.

**Visual evidence:** Your plots show the ball following paths reasonably well, but recall rates are terrible. This suggests the ball is "close but not close enough."

### 3. **Sample Interval Issue**
Your experiment summary shows `Sample Interval: 0s`. This might mean:
- Sampling every frame (could be fine)
- Or a configuration issue

**Check:** In `CSVExperimentRunner`, `recallSampleInterval` should be around `0.05f` to `0.1f` seconds.

### 4. **Spatial Overlap**
If your geometric patterns (`geo_01` through `geo_06`) overlap in space, their wells will directly interfere.

**Check:** Look at your waypoint CSVs - do the patterns share similar X/Z coordinates?

## Solutions (Try in Order)

### Solution 1: Increase Recall Radius Threshold ⭐ **QUICK FIX**

**Location:** `CSVExperimentRunner` → `CSVExperimentConfig` → `recallRadiusThreshold`

**Current:** `3.0f`  
**Try:** `4.0f` to `5.0f`

**Why:** Your visualizations show the ball is following paths, just not staying within 3 units consistently. A larger threshold accounts for noise and slight interference.

**Trade-off:** Less strict recall criteria, but more realistic for noisy/interfering conditions.

---

### Solution 2: Reduce Pattern Interference ⭐ **RECOMMENDED**

**Location:** `LearningImprint` component

**Parameter Adjustments:**

1. **Increase `hypoWidth`** (well width)
   - **Current:** `1.2f`  
   - **Try:** `1.8f` to `2.5f`
   - **Why:** Wider wells are more forgiving and less likely to interfere

2. **Increase `sampleInterval`** (reduce well density)
   - **Current:** `0.05f` (20 samples/second)  
   - **Try:** `0.1f` to `0.15f` (10-6.7 samples/second)
   - **Why:** Fewer wells = less interference

3. **Reduce `learningRate`** (weaker wells)
   - **Current:** `0.2f`  
   - **Try:** `0.1f` to `0.15f`
   - **Why:** Weaker wells interfere less but still provide guidance

**Quick Fix Combination:**
```
hypoWidth: 1.2f → 2.0f
sampleInterval: 0.05f → 0.12f
learningRate: 0.2f → 0.12f
```

---

### Solution 3: Reduce Noise Strength

**Location:** `CSVExperimentRunner` → `CSVExperimentConfig` → `noiseStrength`

**Current:** `1.0f`  
**Try:** `0.5f` to `0.7f`

**Why:** Less noise = ball stays closer to intended path = more samples within threshold.

**Trade-off:** Less realistic recall conditions, but better for testing if interference is the issue.

---

### Solution 4: Increase Landscape Gain

**Location:** `StatePointController` → `landscapeGain`

**Current:** `10f`  
**Try:** `15f` to `25f`

**Why:** Stronger response to learned attractors can help overcome interference from competing patterns.

**Trade-off:** May make ball too sensitive or unstable.

---

### Solution 5: Check Sample Interval

**Location:** `CSVExperimentRunner` → `CSVExperimentConfig` → `recallSampleInterval`

**Should be:** `0.05f` to `0.1f` (not `0f`)

**Why:** Ensures proper sampling during recall tests.

---

### Solution 6: Use Non-Cumulative Mode (If Needed)

If cumulative recall is causing too much interference, consider:

1. **Train all patterns, then test individually** (not cumulative)
   - Set `cumulativeRecallMode = false`
   - Set `runRecallAfterEachPattern = false`
   - This will train all 6 patterns, then test each one individually

2. **Train and test in smaller groups**
   - Train patterns 1-3, test them
   - Clear learning
   - Train patterns 4-6, test them
   - (Requires code modification)

---

## Diagnostic Steps

1. **Check Well Count:**
   - Look in Unity Console for: `"After training pattern 'geo_XX': X total attractors"`
   - If total > 5000, you have severe interference

2. **Check Gradient Strength:**
   - Look for warnings: `"Weak learned gradient detected"`
   - This indicates interference is canceling out gradients

3. **Check Pattern Overlap:**
   - Open your waypoint CSV files
   - Check if patterns share similar X/Z coordinate ranges
   - Overlapping patterns = more interference

4. **Test with Fewer Patterns:**
   - Try with just 2-3 patterns first
   - If recall is good, add more patterns one at a time
   - This helps identify when interference becomes critical

---

## Recommended Immediate Actions

1. **Increase `recallRadiusThreshold`** to `4.5f` (quick test)
2. **Check Unity Console** for well count and gradient warnings
3. **Adjust learning parameters** (Solution 2) to reduce interference
4. **Verify `recallSampleInterval`** is not `0f`

If recall improves with larger threshold, the issue is interference + noise. Focus on Solution 2.

If recall doesn't improve, check:
- Are patterns overlapping spatially?
- Is sample interval configured correctly?
- Are there any errors in Unity Console?

---

## Understanding the Math

With 6 patterns learned cumulatively:
- Each pattern creates ~60-120 wells (depending on `sampleInterval`)
- Total wells: **360-720+ wells**
- If patterns overlap: wells at similar positions create conflicting gradients
- Result: Ball gets "confused" and doesn't follow any single path well

**Solution:** Make wells wider, less dense, and weaker so they don't interfere as much.
