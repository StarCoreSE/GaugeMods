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

namespace Thermodynamics
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]
    public partial class ThermalGrid : MyGameLogicComponent
    {

        private static readonly Guid StorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619343");

        public MyCubeGrid Grid;
        public Dictionary<int, int> PositionToIndex = new Dictionary<int, int>();
        public MyFreeList<ThermalCell> Thermals = new MyFreeList<ThermalCell>();
        public Dictionary<int, float> RecentlyRemoved = new Dictionary<int, float>();
        public ThermalRadiationNode SolarRadiationNode = new ThermalRadiationNode();
        public ThermalRadiationNode WindNode = new ThermalRadiationNode();

        public List<CoolantPump> Pumps = new List<CoolantPump>();

        /// <summary>
        /// current frame per second
        /// </summary>
        public byte FrameCount = 0;

        /// <summary>
        /// update loop index
        /// updates happen across frames
        /// </summary>
        private int SimulationIndex = 0;

        /// <summary>
        /// The number of cells to process in a 1 second interval
        /// </summary>
        private int SimulationQuota = 0;

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
        private int Direction = 1;


        public bool ThermalCellUpdateComplete = true;
        public bool RunningCellUpdate = false;



        public Vector3 FrameWindDirection;
        public Vector3 FrameSolarDirection;
        public MatrixD FrameMatrix;

        public float FrameAmbientTemprature;
        public float FrameAmbientTempratureP4;
        public bool FrameSolarOccluded;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            if (Settings.Instance == null)
            {
                Settings.Instance = Settings.GetDefaults();
            }

            Grid = Entity as MyCubeGrid;

            if (Entity.Storage == null)
                Entity.Storage = new MyModStorageComponent();

            Grid.OnGridSplit += GridSplit;
            Grid.OnGridMerge += GridMerge;

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override bool IsSerialized()
        {
            MyLog.Default.Info($"[{Settings.Name}] serializing");
            Save();
            return base.IsSerialized();
        }
        
        private string Pack()
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

                short t = (short)(c.Temperature);
                bytes[bi + 4] = (byte)t;
                bytes[bi + 5] = (byte)(t >> 8);

                bi += 6;

                //TODO keep track of this. it might cause weird behaivor.
                c.Temperature = t;
            }

            return Convert.ToBase64String(bytes);
        }

        private void Unpack(string data)
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
            catch{ }
        }

        private void Save()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            string data = Pack();

            //string data = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(PackGridInfo()));

            MyModStorageComponentBase storage = Entity.Storage;
            if (storage.ContainsKey(StorageGuid))
            {
                storage[StorageGuid] = data;
            }
            else
            {
                storage.Add(StorageGuid, data);
            }
            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [SAVE] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms, size: {data.Length}");
        }

        private void Load()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            if (Entity.Storage.ContainsKey(StorageGuid))
            {
                Unpack(Entity.Storage[StorageGuid]);
            }

            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [LOAD] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");
        }

        private void BlockAdded(IMySlimBlock b)
        {
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Adding Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            ThermalCellDefinition def = ThermalCellDefinition.GetDefinition(b.BlockDefinition.Id);
            if (def.Ignore) return;

            AddBlockMapping(ref b);
            ThermalCell cell = new ThermalCell(this, b, def);
            cell.AddAllNeighbors();

            int index = Thermals.Allocate();
            PositionToIndex.Add(cell.Id, index);
            Thermals.ItemArray[index] = cell;
            
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            MyLog.Default.Info($"[{Settings.Name}] block removed");

            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Removing Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            //MyLog.Default.Info($"[{Settings.Name}] [{Grid.EntityId}] Removed ({b.Position.Flatten()}) {b.Position}");

            // dont process ignored blocks
            ThermalCellDefinition def = ThermalCellDefinition.GetDefinition(b.BlockDefinition.Id);
            if (def.Ignore) return;

            RemoveBlockMapping(ref b);

            int flat = b.Position.Flatten();
            int index = PositionToIndex[flat];
            ThermalCell cell = Thermals.ItemArray[index];

            if (RecentlyRemoved.ContainsKey(cell.Id))
            {
                RecentlyRemoved[cell.Id] = cell.Temperature;
            }
            else
            {
                RecentlyRemoved.Add(cell.Id, cell.Temperature);
            }

            cell.ClearNeighbors();
            PositionToIndex.Remove(flat);
            Thermals.Free(index);

        }

        private void GridSplit(MyCubeGrid g1, MyCubeGrid g2)
        {
            MyLog.Default.Info($"[{Settings.Name}] Grid Split - G1: {g1.EntityId} G2: {g2.EntityId}");

            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.ItemArray[i];
                if (c == null) continue;

                if (tg1.RecentlyRemoved.ContainsKey(c.Id))
                {
                    c.Temperature = tg1.RecentlyRemoved[c.Id];
                    tg1.RecentlyRemoved.Remove(c.Id);
                }
            }

        }

        private void GridMerge(MyCubeGrid g1, MyCubeGrid g2)
        {

            MyLog.Default.Info($"[{Settings.Name}] Grid Merge - G1: {g1.EntityId} G2: {g2.EntityId}");

            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.ItemArray[i];
                if (c == null) continue;

                int id = c.Block.Position.Flatten();
                if (tg1.PositionToIndex.ContainsKey(id))
                {
                    tg1.Thermals.ItemArray[tg1.PositionToIndex[id]].Temperature = c.Temperature;
                }

            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Grid.Physics == null)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
            }

            Load();
            PrepareNextSimulationStep();
            SimulationQuota = GetSimulationQuota();
        }

        public override void UpdateBeforeSimulation()
        {

            FrameCount++;
            MapSurfaces();
            //MyAPIGateway.Utilities.ShowNotification($"[Loop] f: {MyAPIGateway.Session.GameplayFrameCounter} fc: {FrameCount} sf: {SimulationFrame} sq: {SimulationQuota}", 1, "White");

            // if you are done processing the required blocks this second
            // wait for the start of the next second interval
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

            //MyAPIGateway.Utilities.ShowNotification($"[Loop] c: {count} frameC: {QuotaPerSecond} simC: {60f * QuotaPerSecond}", 1, "White");

            //Stopwatch sw = Stopwatch.StartNew();
            while (FrameQuota >= 1 || SimulationQuota == 0)
            {
                // prepare for the next simulation after a full iteration
                if (SimulationIndex == cellCount || SimulationIndex == -1)
                {
                    // TODO: this is a temperary coolant flow setup it needs to be integrated into the frame quota somehow
                    foreach (CoolantPump pump in Pumps) 
                    {
                        pump.Simulate();
                    }

                    if (!ThermalCellUpdateComplete) 
                    {
                        if (RunningCellUpdate)
                        {
                            RunningCellUpdate = false;
                            ThermalCellUpdateComplete = true;
                        }
                        else 
                        {
                            RunningCellUpdate = true;
                        }
                    }

                    // start a new simulation frame
                    SimulationFrame++;
                    PrepareNextSimulationStep();

                    // reverse the index direction
                    Direction *= -1;
                    // make sure the end cells in the list go once per frame
                    SimulationIndex += Direction;
                }

                //MyLog.Default.Info($"[{Settings.Name}] Frame: {FrameCount} SimFrame: {SimulationFrame}: Index: {SimulationIndex} Quota: {SimulationQuota} FrameQuota:{FrameQuota}");

                ThermalCell cell = Thermals.ItemArray[SimulationIndex];
                if (cell != null)
                {
                    if (RunningCellUpdate)
                    { 
                        cell.UpdateSurfaces(ref ExteriorNodes, ref NodeSurfaces);
                    }

                    cell.Update();
                }

                FrameQuota--;
                SimulationQuota--;
                SimulationIndex += Direction;
            }
            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [UpdateLoop] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");

        }

        private void PrepareNextSimulationStep()
        {

            Vector3D position = Grid.PositionComp.WorldAABB.Center;
            PrepareSolarEnvironment(ref position);
            PrepareEnvironmentTemprature(ref position);
        }

        private void PrepareEnvironmentTemprature(ref Vector3D position) {

            if (!Settings.Instance.EnableEnvironment) return;

            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(position);
            float frameAmbiSquared;
            if (planet == null)
            {
                FrameAmbientTemprature = Settings.Instance.VacuumTemperature;
                frameAmbiSquared = FrameAmbientTemprature * FrameAmbientTemprature;
                FrameAmbientTempratureP4 = frameAmbiSquared * frameAmbiSquared;
            }
            else 
            {
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
                    ambient = def.NightTemperature + ((dot + 1f) * 0.5f * (def.DayTemperature - def.NightTemperature));
                }
                else
                {
                    FrameSolarOccluded = true;
                }

                FrameAmbientTemprature = Math.Max(Settings.Instance.VacuumTemperature, ambient * airDensity);
                frameAmbiSquared = FrameAmbientTemprature * FrameAmbientTemprature;
                FrameAmbientTempratureP4 = frameAmbiSquared * frameAmbiSquared;
                //FrameSolarDecay = 1 - def.SolarDecay * airDensity;
            }

            //FrameWindDirection = Vector3.Cross(planet.GravityComponent.GetWorldGravityNormalized(position), planet.Entity.WorldMatrix.Forward).Normalized() * windSpeed;
            //MySimpleObjectDraw.DrawLine(position, position + FrameWindDirection, MyStringId.GetOrCompute("Square"), ref color2, 0.1f);

            //TODO: implement underground core temparatures
        }

        private void PrepareSolarEnvironment(ref Vector3D position)
        {

            FrameSolarDirection = MyVisualScriptLogicProvider.GetSunDirection();

            if (!Settings.Instance.EnableSolarHeat) return;

            SolarRadiationNode.Update();

            FrameSolarOccluded = false;
            FrameMatrix = Grid.WorldMatrix;

            LineD line = new LineD(position, position + (FrameSolarDirection * 15000000));
            List<MyLineSegmentOverlapResult<MyEntity>> results = new List<MyLineSegmentOverlapResult<MyEntity>>();
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, results);
            LineD subLine;

            for (int i = 0; i < results.Count; i++)
            {
                MyLineSegmentOverlapResult<MyEntity> ent = results[i];
                MyEntity e = ent.Element;

                if (e is MyPlanet)
                {
                    MyPlanet myPlanet = e as MyPlanet;
                    Vector3D planetLocal = position - myPlanet.PositionComp.WorldMatrixRef.Translation;
                    double distance = planetLocal.Length();
                    Vector3D planetDirection = planetLocal / distance;

                    double dot = Vector3D.Dot(planetDirection, FrameSolarDirection);
                    double occlusionDot = Tools.GetLargestOcclusionDotProduct(Tools.GetVisualSize(distance, myPlanet.AverageRadius));

                    if (dot < occlusionDot)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }

                if (e is MyVoxelBase)
                {
                    MyVoxelBase voxel = e as MyVoxelBase;
                    if (voxel.RootVoxel is MyPlanet) continue;

                    voxel.PositionComp.WorldAABB.Intersect(ref line, out subLine);
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                    var green = Color.Green.ToVector4();

                    if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated)
                    {
                        MySimpleObjectDraw.DrawLine(subLine.From, subLine.To, MyStringId.GetOrCompute("Square"), ref green, 0.2f);
                    }

                    IHitInfo hit;
                    MyAPIGateway.Physics.CastRay(subLine.From, subLine.To, out hit, 28); // 28

                    if (hit != null)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }

                if (e is MyCubeGrid && e.Physics != null && e.EntityId != Grid.EntityId)
                {
                    MyCubeGrid g = (e as MyCubeGrid);
                    List<MyCubeGrid> grids = new List<MyCubeGrid>();
                    g.GetConnectedGrids(GridLinkTypeEnum.Physical, grids);

                    for (int j = 0; j < grids.Count; j++)
                    {
                        if (grids[j].EntityId == Grid.EntityId) continue;
                    }

                    g.PositionComp.WorldAABB.Intersect(ref line, out subLine);

                    var blue = Color.Blue.ToVector4();

                    if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated)
                    {

                        MySimpleObjectDraw.DrawLine(subLine.From, subLine.To, MyStringId.GetOrCompute("Square"), ref blue, 0.2f);
                    }

                    Vector3I? hit = (e as MyCubeGrid).RayCastBlocks(subLine.From, subLine.To);

                    if (hit.HasValue)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }
            }

            if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated)
            {
                var color = (FrameSolarOccluded) ? Color.Red.ToVector4() : Color.White.ToVector4();
                var color2 = Color.LightGoldenrodYellow.ToVector4();
                MySimpleObjectDraw.DrawLine(position, position + (FrameSolarDirection * 15000000), MyStringId.GetOrCompute("Square"), ref color, 0.1f);
            }
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
            return 0.00000001f + ((Thermals.UsedLength * Settings.Instance.SimulationSpeed * Settings.Instance.Frequency) / 60f);
        }

        /// <summary>
        /// gets a the thermal cell at a specific location
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public ThermalCell Get(Vector3I position)
        {
            int flat = position.Flatten();
            if (PositionToIndex.ContainsKey(flat))
            {
                return Thermals.ItemArray[PositionToIndex[flat]];
            }

            return null;
        }
    }
}