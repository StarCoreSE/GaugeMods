// using Sandbox.Game;
// using Sandbox.ModAPI;
// using System.Collections.Generic;
// using VRage.Game;
// using VRage.Game.Components;
// using VRageMath;
// using System.Linq;
// using Sandbox.Definitions;
// using Sandbox.Game.EntityComponents;
// using Sandbox.ModAPI;
// using System;
// using System.Collections.Generic;
// using VRage.Game;
// using VRage.Game.ModAPI;
// using VRageMath;
// using VRage.Utils;
// using SpaceEngineers.Game.ModAPI;
// using VRage.ModAPI;
// using Sandbox.Game.Entities;
// using VRage.Game.Components.Interfaces;
// using Sandbox.Game;
// using Sandbox.ModAPI;
// using System.Collections.Generic;
// using VRage.Game;
// using VRage.Game.Components;
// using VRageMath;
// using Sandbox.Game;
// using Sandbox.Game.Entities;
// using Sandbox.Game.EntityComponents;
// using Sandbox.ModAPI;
// using System;
// using System.Collections.Generic;
// using VRage.Game;
// using VRage.Game.Components;
// using VRage.Game.Entity;
// using VRage.Game.ModAPI;
// using VRage.ModAPI;
// using VRage.ObjectBuilders;
// using VRage.Utils;
// using VRageMath;

// namespace Thermodynamics
// {
//     public partial class ThermalGrid : MyGameLogicComponent
//     {
//         // Solar visibility state
//         private Vector3 _lastSolarCheckDirection;
//         private List<ThermalCell> _exposedCellsCache;
//         private long _cacheFrame;

//         /// <summary>
//         /// Check if a block is visible to the sun for solar heating
//         /// </summary>
//         public bool IsBlockSolarVisible(Vector3I blockPos, ref Vector3 solarDirection)
//         {
//             // Check if recalculation is needed based on solar direction change
//             if (Vector3D.Dot(_lastSolarCheckDirection, solarDirection) < Settings.Instance.SolarVisibilityRecalculationThreshold)
//             {
//                 RecalculateSolarVisibility(ref solarDirection);
//             }

//             // Check visibility bit in Surfaces dictionary
//             int surfaceState;
//             if (Surfaces.TryGetValue(blockPos, out surfaceState))
//             {
//                 return (surfaceState & (int)SurfaceFlags.SolarVisible) != 0;
//             }
//             return true; // Default to visible if no surface data
//         }

//         /// <summary>
//         /// Recalculate solar visibility for all blocks using lightweight 2D projection algorithm
//         /// </summary>
//         private void RecalculateSolarVisibility(ref Vector3 solarDirection)
//         {
//             _lastSolarCheckDirection = solarDirection;

//             // Only process grids with exposed surfaces
//             List<ThermalCell> exposedCells = GetExposedCells();
//             if (exposedCells.Count == 0) return;

//             // Clear visibility bits for exposed cells only
//             foreach (ThermalCell cell in exposedCells)
//             {
//                 Vector3I id = Vector3I.Zero;
//                 id.Unflatten(cell.Id);
//                 int surfaceState = Surfaces[id];
//                 Surfaces[id] = surfaceState & ~(int)SurfaceFlags.SolarVisible;
//             }

//             // Transform solar direction to grid-local space
//             Matrix gridInverse = Matrix.Invert(Grid.WorldMatrix);
//             Vector3 localSolar = Vector3.TransformNormal(solarDirection, gridInverse);
//             localSolar.Normalize();

//             // Fast spatial sort using distance bucketing (O(n) instead of O(n log n))
//             Dictionary<int, List<ThermalCell>> distanceBuckets = new Dictionary<int, List<ThermalCell>>();
//             foreach (ThermalCell cell in exposedCells)
//             {
//                 int bucket = (int)(Vector3.Dot(cell.Block.Position, localSolar) / Settings.Instance.ShadowBucketSize);
//                 if (!distanceBuckets.ContainsKey(bucket))
//                     distanceBuckets[bucket] = new List<ThermalCell>();
//                 distanceBuckets[bucket].Add(cell);
//             }

//             // Sort buckets by distance
//             var sortedBuckets = distanceBuckets.OrderBy(kvp => kvp.Key).ToList();

//             // Track recent visible blocks for limited shadowing checks
//             Queue<Vector3I> recentVisibleBlocks = new Queue<Vector3I>();
//             int maxShadowChecks = Settings.Instance.MaxShadowChecksPerBlock;

//             foreach (var bucket in sortedBuckets)
//             {
//                 foreach (ThermalCell cell in bucket.Value)
//                 {
//                     bool isVisible = true;
//                     Vector3I blockPos = Vector3I.Zero;
//                     blockPos.Unflatten(cell.Id);

//                     // Check against only recent visible blocks (bounded O(n*k) instead of O(n²))
//                     foreach (Vector3I shadowPos in recentVisibleBlocks)
//                     {
//                         if (IsBlockShadowing(blockPos, shadowPos, ref localSolar))
//                         {
//                             isVisible = false;
//                             break;
//                         }
//                     }

//                     // Update visibility bit in Surfaces dictionary
//                     int surfaceState = Surfaces[blockPos];
//                     if (isVisible)
//                     {
//                         Surfaces[blockPos] = surfaceState | (int)SurfaceFlags.SolarVisible;

//                         // Maintain limited queue of recent visible blocks
//                         recentVisibleBlocks.Enqueue(blockPos);
//                         if (recentVisibleBlocks.Count > maxShadowChecks)
//                             recentVisibleBlocks.Dequeue();
//                     }
//                 }
//             }
//         }

//         /// <summary>
//         /// Check if block A shadows block B using 2D projection
//         /// </summary>
//         private bool IsBlockShadowing(Vector3I blockPos, Vector3I shadowPos, ref Vector3 localSolar)
//         {
//             // Calculate distances along solar direction
//             float distBlock = Vector3.Dot(blockPos, localSolar);
//             float distShadow = Vector3.Dot(shadowPos, localSolar);

//             // Shadow block must be closer to sun
//             if (distShadow >= distBlock) return false;

//             // Check maximum shadow distance
//             float distance = distBlock - distShadow;
//             if (distance > Settings.Instance.MaxShadowDistance) return false;

//             // Project positions onto plane perpendicular to solar direction
//             Vector3 blockProj = blockPos - distBlock * localSolar;
//             Vector3 shadowProj = shadowPos - distShadow * localSolar;

//             // Check if projections overlap (simple distance-based shadowing)
//             float projDistance = Vector3.Distance(blockProj, shadowProj);
//             float shadowRadius = GetBlockShadowRadius(shadowPos);

//             return projDistance < shadowRadius;
//         }

//         /// <summary>
//         /// Get the shadow radius for a block based on its surface solidity
//         /// </summary>
//         private float GetBlockShadowRadius(Vector3I blockPos)
//         {
//             int surfaceState;
//             if (!Surfaces.TryGetValue(blockPos, out surfaceState))
//                 return Settings.Instance.MinShadowRadius; // Default for unknown blocks

//             // Count solid faces (airtight or have mount points)
//             int solidFaces = 0;

//             // Check each direction for solidity
//             for (int i = 0; i < 6; i++)
//             {
//                 bool isAirtight = (surfaceState & (1 << i)) != 0;
//                 bool hasMountPoint = (surfaceState & (1 << i + (int)SurfaceFlags.SelfMountPointOffset)) != 0;

//                 if (isAirtight || hasMountPoint)
//                     solidFaces++;
//             }

//             // Calculate shadow radius based on solidity
//             // Fully solid block (6 faces) = max radius, open block (0 faces) = min radius
//             float solidityFactor = (float)solidFaces / 6f;
//             float baseRadius = Settings.Instance.BaseShadowRadius;
//             float minRadius = Settings.Instance.MinShadowRadius;

//             return MathHelper.Lerp(minRadius, baseRadius, solidityFactor);
//         }

//         /// <summary>
//         /// Get cached list of cells with exposed surfaces
//         /// </summary>
//         private List<ThermalCell> GetExposedCells()
//         {
//             // Cache this to avoid repeated filtering
//             if (_exposedCellsCache == null || _cacheFrame != SimulationFrame)
//             {
//                 _exposedCellsCache = Thermals.Cells.Where(c => c.ExposedSurfaces > 0).ToList();
//                 _cacheFrame = SimulationFrame;
//             }
//             return _exposedCellsCache;
//         }
//     }
// }
