HOW TO USE EYE-TRACKING DATA WITH THE MAGNET
=============================================

1. PREPARE YOUR EYE-TRACKING DATA
   --------------------------------
   Your CSV file should have this format:
   
   time,x,z
   0.0,0.0,0.0
   0.05,1.2,0.5
   0.1,2.1,1.0
   ...
   
   OR (if using Y instead of Z):
   
   time,x,y
   0.0,0.0,0.0
   0.05,1.2,0.5
   ...
   
   - First column: time in seconds
   - Second column: X position
   - Third column: Z position (or Y if you set "Eye Tracking Use Z" to false)
   - Header row is optional (set "Eye Tracking Has Header" accordingly)

2. PLACE YOUR CSV FILE
   --------------------
   Option A: Put it in Assets/StreamingAssets/ folder
   - Create the folder if it doesn't exist: Assets/StreamingAssets/
   - Place your CSV file there (e.g., "eyetracking_data.csv")
   - In Unity, set the path to just the filename: "eyetracking_data.csv"
   
   Option B: Use absolute path
   - Set the full path to your CSV file (e.g., "C:/Data/eyetracking.csv")

3. CONFIGURE THE MAGNET IN UNITY
   -------------------------------
   a) Select your magnet GameObject (the one with ExternalForceSource component)
   
   b) In the Inspector, set:
      - Path Mode: "Eye Tracking"
      - Eye Tracking CSV Path: your filename (e.g., "eyetracking_data.csv")
      - Eye Tracking Has Header: true (if your CSV has a header row)
      - Eye Tracking Use Z: true (if third column is Z, false if it's Y)
      - Eye Tracking Loop: true/false (whether to repeat the data)
      - Eye Tracking Time Scale: 1.0 (normal speed, 2.0 = 2x speed, etc.)
      - Eye Tracking Scale: adjust to scale your data to world coordinates
      - Eye Tracking Offset: adjust to offset the position in world space

4. EXAMPLE SETTINGS
   -----------------
   If your eye-tracking data is in screen coordinates (0-1920, 0-1080):
   - Eye Tracking Scale: (0.01, 0.01) to convert to world units
   - Eye Tracking Offset: (-9.6, -5.4) to center it (half of scaled size)
   
   If your data is already in world coordinates:
   - Eye Tracking Scale: (1, 1)
   - Eye Tracking Offset: (0, 0)

5. USING FROM CODE
   ---------------
   You can also load eye-tracking data programmatically:
   
   magnet.LoadEyeTrackingData(
       csvPath: "eyetracking_data.csv",
       hasHeader: true,
       useZ: true,
       loop: false,
       timeScale: 1.0f,
       scale: new Vector2(1f, 1f),
       offset: new Vector2(0f, 0f)
   );

6. TIPS
   -----
   - The system automatically interpolates between data points for smooth motion
   - Make sure your time values are in seconds and increase monotonically
   - If playback seems too fast/slow, adjust "Eye Tracking Time Scale"
   - Use "Eye Tracking Scale" to convert from screen/pixel coordinates to world units
   - The magnet will follow the eye-tracking path exactly as recorded



