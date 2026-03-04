import csv
import math

def check_file(filename):
    x_coords = []
    z_coords = []
    
    with open(filename, 'r') as f:
        reader = csv.DictReader(f)
        for row in reader:
            x_coords.append(float(row['x']))
            z_coords.append(float(row['z']))
    
    x_min, x_max = min(x_coords), max(x_coords)
    z_min, z_max = min(z_coords), max(z_coords)
    
    max_dist = max(abs(x_min), abs(x_max), abs(z_min), abs(z_max))
    
    print(f"\n{filename}:")
    print(f"  X range: {x_min:.2f} to {x_max:.2f}")
    print(f"  Z range: {z_min:.2f} to {z_max:.2f}")
    print(f"  Max distance from center: {max_dist:.2f}")
    
    return max_dist

print("Checking pattern sizes...")
test_max = check_file('Assets/StreamingAssets/waypoint_test_patterns_10.csv')
geo_max = check_file('Assets/StreamingAssets/waypoint_geometric_new_10.csv')

print(f"\nComparison:")
print(f"  Test patterns max: {test_max:.2f}")
print(f"  Geo patterns max: {geo_max:.2f}")
print(f"  Ratio: {test_max/geo_max:.2f}x")
