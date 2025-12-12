using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]
    public partial class ThermalGrid : MyGameLogicComponent
    {
        
        public MyCubeGrid Grid;
        public ThermalCellArray Thermals = new ThermalCellArray();
        public Dictionary<int, float> RecentlyRemoved = new Dictionary<int, float>();
        //public ThermalRadiationNode SolarRadiationNode = new ThermalRadiationNode();

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

        /// <summary>
        /// the hottest block on the grid
        /// </summary>
        public ThermalCell HottestBlock;

        /// <summary>
        /// the number of blocks above the critical threshold
        /// </summary>
        public int CriticalBlocks = 0;
        public int CurrentCriticalBlocks = 0;

        /// <summary>
        /// total heat generation for the entire grid
        /// </summary>
        //public float GridHeatGeneration;
        //public float CurrentGridHeatGeneration;

        public long SurfaceUpdateFrame = 0;


        public MatrixD FrameMatrix;
        

        // Pooled collections to reduce GC pressure
        private static List<MyLineSegmentOverlapResult<MyEntity>> _overlapResultPool = new List<MyLineSegmentOverlapResult<MyEntity>>();
        private static List<MyCubeGrid> _gridPool = new List<MyCubeGrid>();



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


            SurfaceCheckComplete += () => {
                SurfaceUpdateFrame = SimulationFrame + 1;
            };

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override bool IsSerialized()
        {
            MyLog.Default.Info($"[{Settings.Name}] serializing");
            Save();
            return base.IsSerialized();
        }

        private void BlockAdded(IMySlimBlock b)
        {
            OnBlockAddOrUpdate(b);
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Adding Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            ThermalCellDefinition def = ThermalCellDefinition.GetDefinition(b.BlockDefinition.Id);
            if (def.IgnoreThermals) return;

            ThermalCell cell = new ThermalCell(this, b, def);
            cell.AddAllNeighbors();

            OnAddDoCoolantCheck(cell);

            Thermals.Add(cell);
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            MyLog.Default.Info($"[{Settings.Name}] block removed");

            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Removing Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            OnBlockRemoved(b);

            //MyLog.Default.Info($"[{Settings.Name}] [{Grid.EntityId}] Removed ({b.Position.Flatten()}) {b.Position}");

            // dont process ignored blocks
            ThermalCellDefinition def = ThermalCellDefinition.GetDefinition(b.BlockDefinition.Id);
            if (def.IgnoreThermals) return;

            int flat = b.Position.Flatten();
            ThermalCell cell = Thermals.GetByPosition(flat);

            if (cell != null)
            {
                OnRemoveDoCoolantCheck(cell);

                if (RecentlyRemoved.ContainsKey(cell.Id))
                {
                    RecentlyRemoved[cell.Id] = cell.Temperature;
                }
                else
                {
                    RecentlyRemoved.Add(cell.Id, cell.Temperature);
                }

                cell.ClearNeighbors();
                Thermals.Remove(flat);
            }
        }

        private void GridSplit(MyCubeGrid g1, MyCubeGrid g2)
        {
            MyLog.Default.Info($"[{Settings.Name}] Grid Split - G1: {g1.EntityId} G2: {g2.EntityId}");

            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.Count; i++)
            {
                ThermalCell c = tg2.Thermals.Cells[i];
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

            for (int i = 0; i < tg2.Thermals.Count; i++)
            {
                ThermalCell c = tg2.Thermals.Cells[i];
                if (c == null) continue;

                int id = c.Block.Position.Flatten();
                ThermalCell targetCell = tg1.Thermals.GetByPosition(id);
                if (targetCell != null)
                {
                    targetCell.Temperature = c.Temperature;
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



        /// <summary>
        /// gets a the thermal cell at a specific location
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public ThermalCell Get(Vector3I position)
        {
            return Thermals.GetByPosition(position.Flatten());
        }


    }
}
