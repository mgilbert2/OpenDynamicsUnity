# Eye Tracking

The eye tracking feature allows you to drive the magnet's movement using real eye tracking data from CSV files. This is useful for experiments and studies where you want to replay recorded gaze patterns.

## CSV Format

Your eye tracking CSV file should have the following format:

```csv
time,x,z
0.0,0.0,0.0
0.05,1.2,0.5
0.1,2.1,1.0
...
```

- **First column**: Time in seconds
- **Second column**: X position
- **Third column**: Z position (or Y if configured)
- **Header row**: Optional (configure via "Eye Tracking Has Header")

## Setup

1. **Place your CSV file** in `Assets/StreamingAssets/` folder, or use an absolute path

2. **Configure the magnet** in Unity:
   - Select the GameObject with the `ExternalForceSource` component
   - Set **Path Mode** to "Eye Tracking"
   - Set **Eye Tracking CSV Path** to your filename (e.g., `example_eyetracking.csv`)
   - Configure other settings:
     - **Eye Tracking Has Header**: `true` if your CSV has a header row
     - **Eye Tracking Use Z**: `true` if third column is Z (false for Y)
     - **Eye Tracking Loop**: Whether to repeat the data
     - **Eye Tracking Time Scale**: Playback speed (1.0 = normal, 2.0 = 2x speed)
     - **Eye Tracking Scale**: Scale multiplier for converting to world coordinates
     - **Eye Tracking Offset**: Position offset in world space

## Example Settings

**For screen coordinates (e.g., 0-1920, 0-1080):**
- Scale: `(0.01, 0.01)` to convert to world units
- Offset: `(-9.6, -5.4)` to center (half of scaled size)

**For world coordinates:**
- Scale: `(1, 1)`
- Offset: `(0, 0)`

## Programmatic Usage

You can also load eye tracking data from code:

```csharp
magnet.LoadEyeTrackingData(
    csvPath: "example_eyetracking.csv",
    hasHeader: true,
    useZ: true,
    loop: false,
    timeScale: 1.0f,
    scale: new Vector2(1f, 1f),
    offset: new Vector2(0f, 0f)
);
```

## Notes

- The system automatically interpolates between data points for smooth motion
- Time values must be in seconds and increase monotonically
- Adjust "Eye Tracking Time Scale" if playback is too fast or slow
- Use "Eye Tracking Scale" to convert from screen/pixel coordinates to world units
- The magnet follows the eye tracking path exactly as recorded



