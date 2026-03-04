#!/usr/bin/env python3
"""
Generate 7 radial line patterns spanning 180 degrees from a horizontal line.
All patterns are straight lines radiating from center (0,0).
60 waypoints per pattern.
"""

import math
import csv

def generate_radial_line(center_x, center_z, size, num_points, angle_degrees):
    """Generate waypoints for a straight line radiating from center at a specific angle."""
    points = []
    angle_rad = math.radians(angle_degrees)
    
    for i in range(num_points):
        # Progress from center outward
        t = i / (num_points - 1)  # 0 to 1
        distance = size * t
        
        x = center_x + distance * math.cos(angle_rad)
        z = center_z + distance * math.sin(angle_rad)
        points.append((x, z))
    
    return points

# Pattern definitions - 7 radial lines spanning 180 degrees from horizontal (0° to 180°)
start_angle = 0    # Start at 0 degrees (horizontal, pointing right/east)
end_angle = 180    # End at 180 degrees (horizontal, pointing left/west)
num_patterns = 7

# Generate angles evenly distributed within the 180-degree range
angles = []
for i in range(num_patterns):
    angle = start_angle + (end_angle - start_angle) * i / (num_patterns - 1)
    angles.append(angle)

patterns = [(f"test_{i+1:02d}", angles[i]) for i in range(num_patterns)]

# Generate CSV
output_file = "Assets/StreamingAssets/waypoint_test_patterns_10.csv"
num_waypoints = 60
center_x, center_z = 0.0, 0.0
size = 9.5  # Target max distance of ~9.5 units

print(f"Generating {len(patterns)} radial line patterns spanning {start_angle}° to {end_angle}° (180° range)...")
print(f"All patterns centered at ({center_x}, {center_z})")
print(f"Output file: {output_file}\n")

with open(output_file, 'w', newline='') as csvfile:
    writer = csv.writer(csvfile)
    writer.writerow(['pattern_id', 'point_index', 'x', 'z'])
    
    for pattern_id, angle in patterns:
        print(f"  Generating {pattern_id} at {angle:.1f} degrees...")
        points = generate_radial_line(center_x, center_z, size, num_waypoints, angle)
        
        for idx, (x, z) in enumerate(points):
            writer.writerow([pattern_id, idx, f"{x:.10f}", f"{z:.10f}"])

print(f"\nDone! Generated {len(patterns)} patterns in {output_file}")
print(f"Angle range: {start_angle}° to {end_angle}° ({end_angle - start_angle}° total)")
print(f"Angles: {', '.join([f'{p[1]:.1f}°' for p in patterns])}")
