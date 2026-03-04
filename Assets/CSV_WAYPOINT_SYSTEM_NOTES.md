# CSV Waypoint System - Implementation Notes

## Overview
This system allows loading waypoint patterns from CSV files and running training/recall experiments. It supports two CSV formats and integrates with the learning system.

---

## Files Created/Modified

### 1. `WaypointPatternCSVLoader.cs`
**Purpose**: Loads waypoint patterns from CSV files in StreamingAssets folder.

**Features**:
- Supports **two CSV formats**:
  - **Multi-pattern format**: `pattern_id,point_index,x,z` (for multiple patterns in one file)
  - **Single-pattern format**: `time,x,z` or `x,z` (for single pattern files like eye-tracking data)
- Automatically detects CSV format by examining headers
- Normalizes pattern IDs (e.g., `pat_1` → `pat_01`)
- Sorts waypoints by `point_index` to ensure correct order

**Key Methods**:
- `Load()` - Loads all patterns from CSV
- `GetPattern(string patternId)` - Returns waypoints for a specific pattern
- `GetAllPatternIds()` - Returns list of all pattern IDs found
- `GetDefaultPatternId()` - Returns default pattern ID (for single-pattern CSVs)

**Usage**:
```csharp
// Get component reference
WaypointPatternCSVLoader loader = GetComponent<WaypointPatternCSVLoader>();

// Load patterns
loader.Load();

// Get specific pattern
List<Vector3> waypoints = loader.GetPattern("pat_01");

// Get all pattern IDs
List<string> allIds = loader.GetAllPatternIds();
```

---

### 2. `ExternalForceSource.cs` (Modified)
**Added Methods**:
- `LoadWaypointPatternFromCSV(string patternId, bool loopWaypoints, bool snapToStart)` - Loads a waypoint pattern from CSV by ID
- `LoadDefaultWaypointPatternFromCSV(bool loopWaypoints, bool snapToStart)` - Loads default pattern from CSV

**Integration**: Added `waypointLoader` field that references a `WaypointPatternCSVLoader` component.

---

### 3. `CSVExperimentRunner.cs` (NEW)
**Purpose**: Runs training and recall experiments for CSV waypoint patterns.

**Features**:
- **Two experiment modes**:
  1. **Per-pattern mode**: Train → Recall → Train → Recall (one pattern at a time)
  2. **Batch mode**: Train all → Recall all (all training first, then all recall tests)

- **Training Phase**:
  - Enables learning
  - Runs pattern through all 60 waypoints
  - Disables learning (preserves learned landscape)
  
- **Recall Test Phase**:
  - Disables learning
  - Enables noise on ball
  - Runs same pattern again
  - Measures recall performance (ball staying within threshold)
  - Calculates pass/fail based on required percentage

- **Recall Rate Summary**: 
  - Tracks all recall results
  - Calculates average, min, max recall rates
  - Shows pass/fail for each pattern
  - Saves summary to `recall_summary.txt`

**Configuration Options**:
- `runRecallAfterEachPattern`: If `true`, recalls after each pattern. If `false`, trains all patterns first, then recalls all.
- `randomizeRecallOrder`: If `true` (and `runRecallAfterEachPattern = false`), recall tests run in random order instead of training order. Training order always stays the same.
- `noiseDelayAfterTraining`: Delay before enabling noise
- `noiseStrength`: Strength of noise during recall
- `recallRadiusThreshold`: Distance ball must stay within (units)
- `recallRequiredPercent`: Required % to pass (0-100)
- `recallSampleInterval`: How often to check recall (seconds)

**Key Methods**:
- `RunExperiment(CSVExperimentConfig config)` - Main experiment coroutine
- `RunTrainingPattern(string patternId, ...)` - Runs one pattern with learning ON
- `RunRecallPattern(string patternId, ...)` - Runs one pattern with noise ON, learning OFF
- `PrintRecallSummary(...)` - Prints recall statistics to console
- `SaveRecallSummaryToFile(...)` - Saves recall summary to file

**Required References**:
- `ball` (StatePointController)
- `magnet` (ExternalForceSource)
- `learningImprint` (LearningImprint)
- `waypointLoader` (WaypointPatternCSVLoader)

---

## CSV Format Details

### Format 1: Multi-Pattern (waypoint_patterns_30.csv)
```
pattern_id,point_index,x,z
pat_01,0,4.312243061518916,3.3788196231305307
pat_01,1,3.764050785498297,3.5684538687666802
pat_02,0,4.296881684423968,4.738657809427956
pat_02,1,3.8198619558800417,5.086190521723894
...
```
- First column: Pattern ID (e.g., `pat_01`, `pat_02`)
- Second column: Point index (0, 1, 2, ...)
- Third column: X coordinate
- Fourth column: Z coordinate

**Note**: Pattern IDs can be entered as `pat_1` or `pat_01` - both will work (auto-normalized).

### Format 2: Single-Pattern (example_eyetracking.csv)
```
time,x,z
0,0,0
0.05,1,0.5
0.1,2,1
...
```
- First column: Time (ignored when extracting waypoints)
- Second column: X coordinate
- Third column: Z coordinate

**Note**: Y coordinate is always 0 (waypoints are on XZ plane).

---

## Experiment Flow

### Mode 1: Recall After Each Pattern (`runRecallAfterEachPattern = true`)

**For each pattern** (pat_01, pat_02, ..., pat_30):
1. **Training**:
   - Learning: **ON**
   - Noise: **OFF**
   - Load pattern (60 waypoints)
   - Run through all 60 waypoints
   - Learning: **OFF** (preserve learned landscape)
   
2. **Recall Test**:
   - Learning: **OFF** (confirmed)
   - Noise: **ON**
   - Reload same pattern
   - Run through all 60 waypoints again
   - Measure recall (ball within threshold?)
   - Log result: `"Recall 'pat_01': 85.3% - PASS ✅"`

### Mode 2: Batch Recall (`runRecallAfterEachPattern = false`)

**Phase 1 - Train All Patterns**:
1. Train pat_01 (Learning: ON, Noise: OFF)
2. Train pat_02 (Learning: ON, cumulative)
3. Train pat_03 (Learning: ON, cumulative)
4. ... (all 30 patterns)
5. Learning: **OFF**

**Phase 2 - Recall Test All Patterns**:
- **If `randomizeRecallOrder = false`** (default):
  1. Enable noise
  2. Recall test pat_01 (Learning: OFF, Noise: ON)
  3. Recall test pat_02 (Learning: OFF, Noise: ON)
  4. Recall test pat_03 (Learning: OFF, Noise: ON)
  5. ... (all 30 patterns in training order)
  6. Noise: **OFF**

- **If `randomizeRecallOrder = true`**:
  1. Enable noise
  2. Recall tests run in **random order** (e.g., pat_15, pat_03, pat_28, pat_01, ...)
  3. Training order is preserved, only recall order is randomized
  4. Uses experiment's random seed for deterministic shuffling
  5. Noise: **OFF**

---

## Setup Instructions

### 1. Prepare CSV File
- Place CSV file in `Assets/StreamingAssets/` folder
- Name it (e.g., `waypoint_patterns_30.csv`)
- Use correct format (see above)

### 2. Create WaypointPatternCSVLoader GameObject
- Right-click Hierarchy → Create Empty
- Name it "Waypoint Loader"
- Add Component → `WaypointPatternCSVLoader`
- Set `csvFileName` to your CSV file name (e.g., `"waypoint_patterns_30.csv"`)

### 3. Assign to Magnet
- Select your Magnet GameObject (ExternalForceSource)
- Find "Waypoint CSV (Stimulus Set)" section
- Drag WaypointLoader GameObject to `waypointLoader` field

### 4. Setup CSVExperimentRunner
- Create Empty GameObject → "CSV Experiment Runner"
- Add Component → `CSVExperimentRunner`
- Assign References:
  - **Ball**: Your ball GameObject
  - **Magnet**: Your magnet GameObject
  - **Learning Imprint**: Your LearningImprint GameObject
  - **Waypoint Loader**: Your WaypointPatternCSVLoader GameObject

### 5. Configure Experiment
- In Inspector → Experiments → Element 0
- **Experiment Settings**:
  - Enabled: ✓
  - Experiment Name: e.g., "CSV_30_Patterns"
  - Random Seed: e.g., `42`
- **CSV Patterns**:
  - Pattern IDs To Run: Leave empty (size = 0) to run all patterns
  - Run Recall After Each Pattern: ✓ (true) or ☐ (false)
  - Randomize Recall Order: ☐ (false) or ✓ (true) - Only applies when "Run Recall After Each Pattern" is false
- **Recall Testing**:
  - Recall Radius Threshold: `1.5`
  - Recall Required Percent: `80%`
  - Recall Sample Interval: `0.05`

---

## Output Files

Experiment results are saved to:
```
[PersistentDataPath]/CSVExperimentLogs/[ExperimentName]_seed[Seed]/
```

**For each pattern**:
- `train_pat_01_[timestamp].csv` - Training trajectory
- `recall_pat_01_[timestamp].csv` - Recall test trajectory

**Summary files**:
- `experiment_summary.txt` - Full experiment configuration
- `recall_summary.txt` - Recall rate statistics (if recall tests run)

---

## Recall Summary Output

After all recall tests complete, you'll see in Console:
```
═══════════════════════════════════════════════════════
           RECALL RATE SUMMARY
═══════════════════════════════════════════════════════
  pat_01: 85.3% - PASS ✅
  pat_02: 72.1% - FAIL ❌
  ...
═══════════════════════════════════════════════════════
  Total Patterns Tested: 30
  Passed: 25  |  Failed: 5
  Pass Rate: 83.3%
  Average Recall Rate: 81.2%
  Min Recall Rate: 65.4%
  Max Recall Rate: 95.8%
  Required Recall: 80.0%
═══════════════════════════════════════════════════════
```

---

## Important Notes

1. **Pattern IDs**: Must match CSV exactly. Use `pat_01`, `pat_02`, etc. (or `pat_1`, `pat_2` - auto-normalized).

2. **Learning**: 
   - Must have `LearningImprint` component with `statePoint` assigned to ball's Transform
   - Learning is only active during training phases
   - Learned landscape is preserved between patterns (unless learning windows are cleared)

3. **Ball Movement**:
   - Ball must have all three references assigned in StatePointController:
     - `driver` (ExternalForceSource - the magnet)
     - `field` (AttractorField)
     - `surface` (PotentialSurface)

4. **Waypoint Order**: Waypoints are sorted by `point_index` to ensure correct traversal order.

5. **Pattern Completion**: Pattern is considered complete when magnet reaches the last waypoint (unless looping is enabled).

---

## Troubleshooting

**"No patterns found!"**
- Check CSV file is in `Assets/StreamingAssets/` folder
- Check `csvFileName` matches actual file name
- Verify CSV format is correct (has header row)

**"Ball doesn't move!"**
- Check ball's StatePointController has all references assigned (driver, field, surface)
- Verify magnet is assigned to ball's `driver` field

**"Learning doesn't work!"**
- Check LearningImprint's `statePoint` is assigned to ball's Transform
- Verify learning is being enabled during training (check console logs)
- Check Learning Rate > 0 in LearningImprint component

**"Pattern not found!"**
- Use exact pattern ID from CSV (e.g., `pat_01` not `pat_1`)
- Or use auto-normalization: `pat_1` will be converted to `pat_01`

---

## Code Changes Summary

### New Files:
- `WaypointPatternCSVLoader.cs` - CSV loading system
- `CSVExperimentRunner.cs` - Experiment execution system

### Modified Files:
- `ExternalForceSource.cs` - Added CSV pattern loading methods
  - `LoadWaypointPatternFromCSV(...)`
  - `LoadDefaultWaypointPatternFromCSV(...)`

### Deleted Files:
- `CSVPatternRunner.cs` - Removed (redundant with CSVExperimentRunner)

---

## Version History

- **Initial Implementation**: Multi-pattern CSV support
- **Added**: Single-pattern CSV support (time,x,z format)
- **Added**: Pattern ID normalization (`pat_1` → `pat_01`)
- **Added**: CSVExperimentRunner with training/recall system
- **Added**: Recall rate summary and statistics
- **Added**: Batch recall mode (train all → recall all)
- **Added**: Random recall order option (shuffles recall tests while keeping training order)
- **Fixed**: Learning/Noise state management during experiments

---

## Future Enhancements (Potential)

- Export learned landscape to file
- Visual debug display of learned attractors
- Pattern randomization order option
- Custom recall test patterns (different from training patterns)
- Real-time recall rate display during experiment
