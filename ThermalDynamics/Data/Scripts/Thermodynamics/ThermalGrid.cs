using ProtoBuf.Meta;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Voxels;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Transactions;
using System.Xml;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;


namespace Thermodynamics {
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]
    public partial class ThermalGrid : MyGameLogicComponent {

        static readonly Guid StorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619343");

        public MyCubeGrid Grid;
        public Dictionary<int, int> PositionToIndex = new Dictionary<int, int>();
        public MyFreeList<ThermalCell> Thermals = new MyFreeList<ThermalCell>();
        public Dictionary<int, float> RecentlyRemoved = new Dictionary<int, float>();
        public ThermalRadiationNode SolarRadiationNode = new ThermalRadiationNode();
        public ThermalRadiationNode WindNode = new ThermalRadiationNode();

        /// <summary>
        /// current frame per second
        /// </summary>
        public byte FrameCount = 0;

        /// <summary>
        /// update loop index
        /// updates happen across frames
        /// </summary>
        int SimulationIndex = 0;

        /// <summary>
        /// The number of cells to process in a 1 second interval
        /// </summary>
        int SimulationQuota = 0;

        /// <summary>
        /// The fractional number of cells to process this frame
        /// the remainder is carried over to the next frame
        /// </summary>
        public float FrameQuota = 0;

        /// <summary>
        /// The total number of simulations since grid life
        /// </summary>
        public long SimulationFrame = 1;

        /// <summary>
        /// updates cycle between updating first to last, last to first
        /// this ensures an even distribution of heat.
        /// </summary>
        int Direction = 1;


        public bool ThermalCellUpdateComplete = true;


        public Vector3 FrameWindDirection;
        public Vector3 FrameSolarDirection;
        public MatrixD FrameMatrix;

        public float FrameAmbientTemprature;
        public float FrameAmbientTempratureP4;
        public float FrameSolarDecay;
        public bool FrameSolarOccluded;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            if (Settings.Instance == null)
            {
                Settings.Instance = Settings.GetDefaults();
            }

            Grid = this.Entity as MyCubeGrid;

            if (this.Entity.Storage == null) this.Entity.Storage = new MyModStorageComponent();

            if (Grid != null)
            {
                Grid.OnGridSplit += GridSplit;
                Grid.OnGridMerge += GridMerge;

                Grid.OnBlockAdded += BlockAdded;
                Grid.OnBlockRemoved += BlockRemoved;
            }

            this.NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override bool IsSerialized()
        {
            MyLog.Default.Info($"[{Settings.Name}] serializing");
            Save();
            return base.IsSerialized();
        }

        string Pack()
        {
            byte[] bytes = new byte[Thermals.Count * 6];

            int bi = 0;
            for (int i = 0; i < Thermals.UsedLength; i++)
            {
                ThermalCell c = Thermals.ItemArray[i];
                if (c == null) continue;

                int id = c.Id;
                bytes[bi] = (byte)id;
                bytes[bi + 1] = (byte)(id >> 8);
                bytes[bi + 2] = (byte)(id >> 16);
                bytes[bi + 3] = (byte)(id >> 24);

                short t = (short)c.Temperature;
                bytes[bi + 4] = (byte)t;
                bytes[bi + 5] = (byte)(t >> 8);

                bi += 6;

                // Track temperature changes by storing previous value
                c.PreviousTemperature = c.Temperature;// Added tracking
                c.Temperature = t;// Update current temperature

                // Log significant temperature changes for debugging
                // Probably use this for error correction or validation but whatever
                //    if (Math.Abs(c.PreviousTemperature - t) > Settings.SignificantTempChange)
                //    {
                //        MyLog.Default.Debug($"[{Settings.Name}] Significant temperature change in cell {c.Id}: {c.PreviousTemperature} -> {t}");
                //    }
            }

            return Convert.ToBase64String(bytes);
        }

        void Unpack(string data)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(data);

                for (int i = 0; i < bytes.Length; i += 6)
                {
                    int id = bytes[i];
                    id |= bytes[i + 1] << 8;
                    id |= bytes[i + 2] << 16;
                    id |= bytes[i + 3] << 24;

                    int f = bytes[i + 4];
                    f |= bytes[i + 5] << 8;

                    Thermals.ItemArray[PositionToIndex[id]].Temperature = f;
                }

                //MyLog.Default.Info($"[{Settings.Name}] [Unpack] {id} {PositionToIndex[id]} {Thermals.list[PositionToIndex[id]].Block.BlockDefinition.Id} - T: {f}");
            }
            catch (Exception)
            {
                // ignored
            }
        }

        void Save()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            string data = Pack();

            //string data = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(PackGridInfo()));

            MyModStorageComponentBase storage = this.Entity.Storage;
            storage[StorageGuid] = data;
            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [SAVE] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms, size: {data.Length}");
        }

        void Load()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            if (this.Entity.Storage.ContainsKey(StorageGuid))
            {
                Unpack(this.Entity.Storage[StorageGuid]);
            }

            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [LOAD] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");
        }

        void BlockAdded(IMySlimBlock b)
        {
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Adding Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            AddBlockMapping(ref b);
            ThermalCell cell = new ThermalCell(this, b);
            cell.AddAllNeighbors();

            int index = Thermals.Allocate();
            PositionToIndex.Add(cell.Id, index);
            Thermals.ItemArray[index] = cell;
        }

        void BlockRemoved(IMySlimBlock b)
        {
            MyLog.Default.Info($"[{Settings.Name}] block removed");

            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Removing Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            //MyLog.Default.Info($"[{Settings.Name}] [{Grid.EntityId}] Removed ({b.Position.Flatten()}) {b.Position}");

            RemoveBlockMapping(ref b);

            int flat = b.Position.Flatten();
            int index = PositionToIndex[flat];
            ThermalCell cell = Thermals.ItemArray[index];

            RecentlyRemoved[cell.Id] = cell.Temperature;

            cell.ClearNeighbors();
            PositionToIndex.Remove(flat);
            Thermals.Free(index);

        }

        void GridSplit(MyCubeGrid g1, MyCubeGrid g2)
        {
            MyLog.Default.Info($"[{Settings.Name}] Grid Split - G1: {g1.EntityId} G2: {g2.EntityId}");

            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.ItemArray[i];
                if (c == null) continue;

                float value;
                if (!tg1.RecentlyRemoved.TryGetValue(c.Id, out value)) continue;
                c.Temperature = value;
                tg1.RecentlyRemoved.Remove(c.Id);
            }

        }

        void GridMerge(MyCubeGrid g1, MyCubeGrid g2)
        {

            MyLog.Default.Info($"[{Settings.Name}] Grid Merge - G1: {g1.EntityId} G2: {g2.EntityId}");

            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.ItemArray[i];
                if (c == null) continue;

                int id = c.Block.Position.Flatten();
                int value;
                if (tg1.PositionToIndex.TryGetValue(id, out value))
                {
                    tg1.Thermals.ItemArray[value].Temperature = c.Temperature;
                }

            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Grid.Physics == null)
            {
                this.NeedsUpdate = MyEntityUpdateEnum.NONE;
            }

            Load();
            PrepareNextSimulationStep();
            SimulationQuota = GetSimulationQuota();
        }


        public override void UpdateBeforeSimulation()
        {
            try
            {
                FrameCount++;

                if (SimulationQuota == 0)
                {
                    if (FrameCount >= 60)
                    {
                        SimulationQuota = GetSimulationQuota();
                        FrameCount = 0;
                        FrameQuota = 0;
                    }
                    else
                    {
                        return;
                    }
                }

                FrameQuota += GetFrameQuota();
                int cellCount = Thermals.UsedLength;

                while(FrameQuota >= 1)
                {
                    if (SimulationQuota == 0) break;

                    if (SimulationIndex == cellCount || SimulationIndex == -1)
                    {
                        if (!ThermalCellUpdateComplete)
                            ThermalCellUpdateComplete = true;

                        SimulationFrame++;

                        MapSurfaces();
                        PrepareNextSimulationStep();

                        Direction *= -1;
                        SimulationIndex += Direction;
                    }

                    try
                    {
                        ThermalCell cell = Thermals.ItemArray[SimulationIndex];
                        if (cell != null)
                        {
                            if (!ThermalCellUpdateComplete)
                            {
                                cell.UpdateSurfaces(ref ExteriorNodes, ref NodeSurfaces);
                            }

                            cell.Update();
                        }
                    }
                    catch (Exception ex)
                    {
                        MyLog.Default.Error($"Error updating thermal cell at index {SimulationIndex}: {ex}");
                    }

                    FrameQuota--;
                    SimulationQuota--;
                    SimulationIndex += Direction;
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.Error($"Critical error in thermal grid update: {ex}");
            }
        }
        void PrepareNextSimulationStep()
        {

            Vector3D position = Grid.PositionComp.WorldAABB.Center;
            PrepareEnvironmentTemprature(ref position);
            PrepareSolarEnvironment(ref position);
        }

        void PrepareEnvironmentTemprature(ref Vector3D position)
        {
            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(position);
            if (planet == null) return;

            bool isUnderground = false;
            PlanetDefinition def = planet.Definition();
            Vector3 local = position - planet.Position;
            Vector3D surfacePointLocal = planet.Entity.GetClosestSurfacePointLocal(ref local);
            isUnderground = local.LengthSquared() < surfacePointLocal.LengthSquared();
            float airDensity = planet.Entity.GetAirDensity(position);
            float windSpeed = planet.Entity.GetWindSpeed(position);

            float ambient = def.UndergroundTemperature;
            if (!isUnderground)
            {
                float dot = (float)Vector3D.Dot(Vector3D.Normalize(local), FrameSolarDirection);
                ambient = def.NightTemperature + (dot + 1f) * 0.5f * (def.DayTemperature - def.NightTemperature);
            }
            else
            {
                // Implement underground core temperatures
                float distanceToCore = (float)local.Length();
                float coreTemp = def.CoreTemperature;// Added to PlanetDefinition
                float surfaceDistance = (float)surfacePointLocal.Length();

                // Calculate temperature gradient based on depth
                float depthRatio = 1 - (distanceToCore / surfaceDistance);
                ambient = def.UndergroundTemperature + (coreTemp - def.UndergroundTemperature) * depthRatio;

                FrameSolarOccluded = true;
            }

            FrameAmbientTemprature = Math.Max(2.7f, ambient * airDensity);
            float frameAmbiSquared = FrameAmbientTemprature * FrameAmbientTemprature;
            FrameAmbientTempratureP4 = frameAmbiSquared * frameAmbiSquared;
            FrameSolarDecay = 1 - def.SolarDecay * airDensity;
        }

        void PrepareSolarEnvironment(ref Vector3D position)
        {
            // Early exit if solar heating is disabled
            if (!Settings.Instance.EnableSolarHeat) return;

            // Update radiation node and initialize frame values
            SolarRadiationNode.Update();
            FrameSolarOccluded = false;
            FrameSolarDirection = MyVisualScriptLogicProvider.GetSunDirection();
            FrameMatrix = Grid.WorldMatrix;

            // Constants for ray casting
            const double maxSolarDistance = 15000000;
            const int rayCastDistance = 28;

            // Create the primary ray for solar occlusion checking
            LineD solarRay = new LineD(position, position + FrameSolarDirection * (float)maxSolarDistance);

            // Cache results list to avoid repeated allocations
            List<MyLineSegmentOverlapResult<MyEntity>> results = new List<MyLineSegmentOverlapResult<MyEntity>>(32);
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref solarRay, results);

            // Sort results by distance to improve early-out performance
            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            foreach (var result in results)
            {
                MyEntity entity = result.Element;

                // Skip null or invalid entities
                if (entity?.Physics == null) continue;

                // Handle planets
                MyPlanet planet = entity as MyPlanet;
                if (planet != null)
                {
                    if (CheckPlanetOcclusion(ref position, planet))
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                    continue;
                }

                // Handle voxel entities
                MyVoxelBase voxel = entity as MyVoxelBase;
                if (voxel != null && !(voxel.RootVoxel is MyPlanet))
                {
                    if (CheckVoxelOcclusion(ref solarRay, voxel, rayCastDistance))
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                    continue;
                }

                // Handle grid entities
                MyCubeGrid grid = entity as MyCubeGrid;
                if (grid == null) continue;
                if (!CheckGridOcclusion(ref solarRay, grid)) continue;
                FrameSolarOccluded = true;
                break;
            }

            // Debug visualization
            DrawDebugLines(ref position, ref solarRay);
        }

        private bool CheckPlanetOcclusion(ref Vector3D position, MyPlanet planet)
        {
            Vector3D planetLocal = position - planet.PositionComp.WorldMatrixRef.Translation;
            double distance = planetLocal.Length();

            // Skip if too far from planet
            if (distance > planet.AverageRadius * 2)
                return false;

            Vector3D planetDirection = planetLocal / distance;
            double dot = Vector3D.Dot(planetDirection, FrameSolarDirection);
            double occlusionDot = Tools.GetLargestOcclusionDotProduct(
                Tools.GetVisualSize(distance, planet.AverageRadius));

            return dot < occlusionDot;
        }

        private bool CheckVoxelOcclusion(ref LineD ray, MyVoxelBase voxel, int raycastDistance)
        {
            LineD subLine;
            voxel.PositionComp.WorldAABB.Intersect(ref ray, out subLine);

            // Debug visualization for voxel intersection
            if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated)
            {
                Vector4 green = Color.Green.ToVector4();
                MySimpleObjectDraw.DrawLine(subLine.From, subLine.To,
                    MyStringId.GetOrCompute("Square"), ref green, 0.2f);
            }

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(subLine.From, subLine.To, out hit, raycastDistance);
            return hit != null;
        }

        private bool CheckGridOcclusion(ref LineD ray, MyCubeGrid grid)
        {
            // Skip self-occlusion
            if (grid.EntityId == Grid.EntityId) return false;

            // Check connected grids
            List<MyCubeGrid> connectedGrids = new List<MyCubeGrid>();
            grid.GetConnectedGrids(GridLinkTypeEnum.Physical, connectedGrids);

            for (int i = 0; i < connectedGrids.Count; i++)
            {
                if (connectedGrids[i].EntityId == Grid.EntityId)
                    return false;
            }

            LineD subLine;
            grid.PositionComp.WorldAABB.Intersect(ref ray, out subLine);

            // Debug visualization for grid intersection
            if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated)
            {
                Vector4 blue = Color.Blue.ToVector4();
                MySimpleObjectDraw.DrawLine(subLine.From, subLine.To,
                    MyStringId.GetOrCompute("Square"), ref blue, 0.2f);
            }

            Vector3I? hit = grid.RayCastBlocks(subLine.From, subLine.To);
            return hit.HasValue;
        }

        private void DrawDebugLines(ref Vector3D position, ref LineD solarRay)
        {
            if (!Settings.Debug || MyAPIGateway.Utilities.IsDedicated)
                return;

            Vector4 rayColor = FrameSolarOccluded ? Color.Red.ToVector4() : Color.White.ToVector4();
            MySimpleObjectDraw.DrawLine(position, solarRay.To,
                MyStringId.GetOrCompute("Square"), ref rayColor, 0.1f);
        }
        /// <summary>
        /// Calculates thermal cell count second to match the desired simulation speed
        /// </summary>
        public int GetSimulationQuota()
        {
            return Math.Max(1, (int)(Thermals.UsedLength * Settings.Instance.SimulationSpeed * Settings.Instance.Frequency));
        }

        /// <summary>
        /// Calculates the thermal cell count required each frame
        /// </summary>
        public float GetFrameQuota()
        {
            return 0.00000001f + Thermals.UsedLength * Settings.Instance.SimulationSpeed * Settings.Instance.Frequency / 60f;
        }

        /// <summary>
        /// gets the thermal cell at a specific location
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public ThermalCell Get(Vector3I position)
        {
            int flat = position.Flatten();
            int value;
            return PositionToIndex.TryGetValue(flat, out value) ? Thermals.ItemArray[value] : null;

        }
    }
}