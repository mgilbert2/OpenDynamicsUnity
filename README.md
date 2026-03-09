# Attractors - Unity Learning System

A Unity-based attractor learning system that learns patterns through Gaussian potential wells and tests recall performance with interference analysis.

## Overview

This project implements a learning system where a ball follows patterns (guided by a magnet) and learns attractor wells along the path. The system can learn multiple patterns and test recall performance, including analysis of catastrophic forgetting and pattern interference.

## Features

- **Pattern Learning**: Gaussian attractor wells learned along training paths
- **Recall Testing**: Tests how well learned patterns can be recalled
- **Cumulative Learning**: Can learn multiple patterns and test all of them
- **Interference Analysis**: Visualizes how new patterns interfere with old ones
- **Depth Normalization**: Optional depth normalization to prevent unbounded growth
- **R Visualization Scripts**: Comprehensive analysis tools for experiment results

## Project Structure

```
Attractors/
├── Assets/                    # Unity project assets
│   ├── LearningImprint.cs    # Core learning system
│   ├── CSVExperimentRunner.cs # Experiment automation
│   └── ...                   # Other Unity scripts
├── *.R                        # R visualization scripts
├── *.py                       # Python utility scripts
└── README.md                  # This file
```

## Key Components

### Unity Scripts

- **LearningImprint.cs**: Implements the attractor learning system with Gaussian wells
- **CSVExperimentRunner.cs**: Automates training and recall experiments
- **StatePointController.cs**: Controls ball physics and movement
- **AttractorField.cs**: Manages attractor field calculations

### R Analysis Scripts

- **VisualizeOverallAccuracy.R**: Overall system performance analysis
- **combinedrecalloverlays.R**: Detailed path visualization with interference
- **VisualizeForgettingCurves.R**: Individual pattern forgetting analysis
- **VisualizePaths.R**: Flexible path visualization tool

## Setup

### Unity Requirements

- Unity 2021.3 or later (check ProjectSettings/ProjectVersion.txt)
- No special packages required (uses built-in Unity features)

### R Requirements

Install required R packages:
```r
install.packages(c("ggplot2", "dplyr", "gridExtra", "RColorBrewer"))
```

## Usage

### Running Experiments

1. Set up patterns in CSV format (waypoint files)
2. Configure experiment in Unity Inspector:
   - Set `CSVExperimentRunner` component
   - Configure experiment parameters
   - Set learning parameters (depth cap, normalization, etc.)
3. Run experiment - results saved to `CSVExperimentLogs/`

### Analyzing Results

1. Run R scripts to visualize results:
```r
source("VisualizeOverallAccuracy.R")
createOverallAccuracyPlot(folder = "path/to/experiment")
```

2. Check generated plots and CSV summaries

## Key Parameters

See `CRITICAL_PARAMETERS.md` for detailed parameter documentation.

**Most Important:**
- `learningRate`: How fast learning occurs (0.2-0.4)
- `hypoDepth`: Attractor strength (10-20)
- `hypoWidth`: Attractor spread (1.2-2.0)
- `landscapeGain`: How strongly ball follows learned paths (10-15)
- `maxWellDepth`: Cap on individual well depth (0 = no limit)
- `normalizeDepth`: Enable depth normalization

## Recent Changes

- **Depth Normalization**: Added automatic depth normalization to prevent unbounded growth
- **Experiment Config**: Depth cap and normalization now configurable per-experiment
- **Interference Visualization**: R scripts show all learned patterns in background

## License


## Contributing


## Citation

