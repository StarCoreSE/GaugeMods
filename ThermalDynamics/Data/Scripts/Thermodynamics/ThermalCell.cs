using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.Utils;
using SpaceEngineers.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.Components.Interfaces;

namespace Thermodynamics
{
    public class ThermalCell
    {
        public int Id;
        public long Frame;

        public float Temperature;
        public float LastTemprature;
        public float DeltaTemperature;

        public float EnergyProduction;
        public float EnergyConsumption;
        public float ThrustEnergyConsumption;
        public float HeatGeneration;

        public float k;
        public float C; // c =  Temp / (watt * meter)
        public float Mass; // kg
        public float Area; // m^2
        public float ExposedSurfaceArea; // m^2 of all exposed faces on this block
        public float ThermalMassInv; // 1 / SpecificHeat * Mass
        public float Boltzmann;
        public float[] kA;


        public ThermalGrid Grid;
        public IMySlimBlock Block;
        public ThermalCellDefinition Definition;

        public List<ThermalCell> Neighbors = new List<ThermalCell>();
        public List<int> TouchingSerfacesByNeighbor = new List<int>();

        public int ExposedSurfaces = 0;
        private int[] ExposedSurfacesByDirection = new int[6];
        //public List<Vector3I> ExposedSurfaceDirections = new List<Vector3I>();

        public ThermalCell(ThermalGrid g, IMySlimBlock b, ThermalCellDefinition def)
        {
            Grid = g;
            Block = b;
            Id = b.Position.Flatten();
            Definition = def;

            //TODO: the listeners need to handle changes at the end
            //of the update cycle instead of whenever.
            SetupListeners();

            Mass = Block.Mass;

            //MyLog.Default.Info($"[{Settings.Name}] {Block.BlockDefinition.Id} -- mass: {Mass}");
            
            Area = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;
            C = 1 / (Definition.SpecificHeat * Mass * Block.CubeGrid.GridSize);
            ThermalMassInv = 1f / (Definition.SpecificHeat * Mass);
            Boltzmann = -1 * Definition.Emissivity * Tools.BoltzmannConstant;

            float value =  (Definition.SpecificHeat * Mass * Block.CubeGrid.GridSize) / (6 * Area * ((Block.Max + 1) - Block.Min).LargestFace());
            k = value * Definition.Conductivity;
            Definition.Conductivity = value;

            //MyLog.Default.Info($"face: {(Block.Max + 1) - Block.Min} -- {((Block.Max + 1) - Block.Min).LargestFace()} -- {(6 * Area * ((Block.Max + 1) - Block.Min).LargestFace())} --- other: {(Definition.SpecificHeat * Mass * Block.CubeGrid.GridSize)}");
            UpdateHeat();
        }

        private void SetupListeners()
        {
            if (Block.FatBlock == null) return;

            IMyCubeBlock fat = Block.FatBlock;
            if (fat is IMyThrust)
            {
                IMyThrust thrust = (fat as IMyThrust);
                thrust.ThrustChanged += OnThrustChanged;
                OnThrustChanged(thrust, 0, thrust.CurrentThrust);

            }
            else
            {
                fat.Components.ComponentAdded += OnComponentAdded;
                fat.Components.ComponentRemoved += OnComponentRemoved;

                if (fat.Components.Contains(typeof(MyResourceSourceComponent)))
                {
                    fat.Components.Get<MyResourceSourceComponent>().OutputChanged += PowerProducedChanged;
                }

                if (fat.Components.Contains(typeof(MyResourceSinkComponent)))
                {
                    fat.Components.Get<MyResourceSinkComponent>().CurrentInputChanged += PowerConsumedChanged;
                }
            }

            if (fat is IMyPistonBase)
            {
                (fat as IMyPistonBase).AttachedEntityChanged += GridGroupChanged;
            }
            else if (fat is IMyMotorBase)
            {
                (fat as IMyMotorBase).AttachedEntityChanged += GridGroupChanged;
            }
            else if (fat is IMyLandingGear)
            {
                // had to use this crappy method because the better method is broken
                // KEEN!!! fix your code please!
                IMyLandingGear gear = (fat as IMyLandingGear);
                gear.StateChanged += (state) =>
                {
                    ThermalCell c = Grid.Get(gear.Position);
                    IMyEntity entity = gear.GetAttachedEntity();

                    // if the entity is not MyCubeGrid reset landing gear neighbors because we probably detached
                    if (!(entity is MyCubeGrid))
                    {
                        c.AddAllNeighbors();
                        return;
                    }

                    // get the search area
                    MyCubeGrid grid = entity as MyCubeGrid;
                    ThermalGrid gtherms = grid.GameLogic.GetAs<ThermalGrid>();

                    Vector3D oldMin = gear.CubeGrid.GridIntegerToWorld(new Vector3I(gear.Min.X, gear.Min.Y, gear.Min.Z));
                    Vector3D oldMax = gear.CubeGrid.GridIntegerToWorld(new Vector3I(gear.Max.X, gear.Max.Y, gear.Min.Z));

                    oldMax += gear.WorldMatrix.Down * (grid.GridSize + 0.2f);

                    Vector3I min = grid.WorldToGridInteger(oldMin);
                    Vector3I max = grid.WorldToGridInteger(oldMax);

                    //MyLog.Default.Info($"[{Settings.Name}] min {min} max {max}");

                    // look for active cells on the other grid that are inside the search area
                    Vector3I temp = Vector3I.Zero;
                    for (int x = min.X; x <= max.X; x++)
                    {
                        temp.X = x;
                        for (int y = min.Y; y <= max.Y; y++)
                        {
                            temp.Y = y;
                            for (int z = min.Z; z <= max.Z; z++)
                            {
                                temp.Z = z;

                                ThermalCell ncell = gtherms.Get(temp);
                                //MyLog.Default.Info($"[{Settings.Name}] testing {temp} {ncell != null}");
                                if (ncell != null)
                                {
                                    c.AddNeighbor(ncell);
                                }
                            }
                        }
                    }

                    c.CalculatekA();

                };
            }
        }

        private void OnComponentAdded(Type compType, IMyEntityComponentBase component)
        {
            if (compType == typeof(MyResourceSourceComponent))
            {
                (component as MyResourceSourceComponent).OutputChanged += PowerProducedChanged;
            }

            if (compType == typeof(MyResourceSinkComponent))
            {
                (component as MyResourceSinkComponent).CurrentInputChanged += PowerConsumedChanged;
            }
        }

        private void OnComponentRemoved(Type compType, IMyEntityComponentBase component)
        {
            if (compType == typeof(MyResourceSourceComponent))
            {
                (component as MyResourceSourceComponent).OutputChanged -= PowerProducedChanged;
            }

            if (compType == typeof(MyResourceSinkComponent))
            {
                (component as MyResourceSinkComponent).CurrentInputChanged -= PowerConsumedChanged;
            }
        }

        private void GridGroupChanged(IMyMechanicalConnectionBlock block)
        {
            ThermalGrid g = block.CubeGrid.GameLogic.GetAs<ThermalGrid>();
            ThermalCell cell = g.Get(block.Position);

            if (cell == null) return;

            //MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell {block.Position} IsAttached: {block.IsAttached} DoubleCheck: {block.Top != null}");

            if (block.Top == null)
            {
                for (int i = 0; i < cell.Neighbors.Count; i++)
                {
                    ThermalCell ncell = cell.Neighbors[i];

                    if (ncell.Block.CubeGrid == cell.Block.CubeGrid) continue;

                    //MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} removed connection to, Grid: {ncell.Block.CubeGrid.EntityId} Cell: {ncell.Block.Position}");

                    cell.RemoveNeighbor(ncell);
                    break;
                }
            }
            else
            {
                ThermalCell ncell = block.Top.CubeGrid.GameLogic.GetAs<ThermalGrid>().Get(block.Top.Position);

                //MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} adding connection to, nGrid: {ncell.Block.CubeGrid.EntityId} nCell: {ncell.Block.Position}");

                cell.AddNeighbor(ncell);
            }
        }

        private void OnThrustChanged(IMyThrust block, float old, float current)
        {
            MyThrustDefinition def = block.SlimBlock.BlockDefinition as MyThrustDefinition;

            ThrustEnergyConsumption = def.ForceMagnitude * (block.CurrentThrust / block.MaxThrust);
            UpdateHeat();
        }

        /// <summary>
        /// Adjusts heat generation based on consumed power 
        /// </summary>
        private void PowerConsumedChanged(MyDefinitionId resourceTypeId, float oldInput, MyResourceSinkComponent sink)
        {
            try
            {
                if (resourceTypeId == MyResourceDistributorComponent.ElectricityId)
                {
                    EnergyConsumption = sink.CurrentInputByType(resourceTypeId) * Tools.MWtoWatt;
                    UpdateHeat();
                }
            }
            catch { }
        }

        private void PowerProducedChanged(MyDefinitionId resourceTypeId, float oldOutput, MyResourceSourceComponent source)
        {
            try
            {
                if (resourceTypeId == MyResourceDistributorComponent.ElectricityId)
                {
                    EnergyProduction = source.CurrentOutputByType(resourceTypeId) * Tools.MWtoWatt;
                    UpdateHeat();
                }
            }
            catch { }
        }

        public void ClearNeighbors()
        {
            for (int i = 0; i < Neighbors.Count; i++)
            {
                ThermalCell ncell = Neighbors[i];
                int j = ncell.Neighbors.IndexOf(this);
                if (j != -1)
                {
                    ncell.Neighbors.RemoveAt(j);
                    ncell.TouchingSerfacesByNeighbor.RemoveAt(j);
                    ncell.CalculatekA();
                }
            }

            Neighbors.Clear();
            TouchingSerfacesByNeighbor.Clear();
            CalculatekA();
        }

        public void AddAllNeighbors()
        {
            ClearNeighbors();
            //get a list of current neighbors from the grid
            List<IMySlimBlock> neighbors = new List<IMySlimBlock>();
            Block.GetNeighbours(neighbors);

            for (int i = 0; i < neighbors.Count; i++)
            {
                IMySlimBlock n = neighbors[i];
                ThermalCell ncell = Grid.Get(n.Position);
                AddNeighbor(ncell);
            }
        }

        protected void AddNeighbor(ThermalCell n2)
        {
            //MyLog.Default.Info($"[Thermals] AddNeighbours");
            Neighbors.Add(n2);
            n2.Neighbors.Add(this);

            int area = FindSurfaceArea(n2);
            TouchingSerfacesByNeighbor.Add(area);
            n2.TouchingSerfacesByNeighbor.Add(area);

            CalculatekA();
            n2.CalculatekA();
        }

        /// <summary>
        /// Maps out the surface area between two blocks
        /// </summary>
        /// <returns>area in grid units (not meters)</returns>
        protected int FindSurfaceArea(ThermalCell neighbor)
        {

            int index = Neighbors.IndexOf(neighbor);
            int totalArea = 0;

            Vector3I position = Block.Position;
            List<MyCubeBlockDefinition.MountPoint> mounts = new List<MyCubeBlockDefinition.MountPoint>();
            MyBlockOrientation orientation = Block.Orientation;
            MyCubeBlockDefinition def = (MyCubeBlockDefinition)Block.BlockDefinition;
            MyCubeGrid.TransformMountPoints(mounts, def, def.MountPoints, ref orientation);

            Vector3I nposition = neighbor.Block.Position;
            List<MyCubeBlockDefinition.MountPoint> neighborMounts = new List<MyCubeBlockDefinition.MountPoint>();
            MyBlockOrientation neighborOrientation = neighbor.Block.Orientation;
            MyCubeBlockDefinition neighborDef = (MyCubeBlockDefinition)neighbor.Block.BlockDefinition;
            MyCubeGrid.TransformMountPoints(neighborMounts, neighborDef, neighborDef.MountPoints, ref neighborOrientation);

            Vector3I p = nposition - position;
            for (int i = 0; i < mounts.Count; i++)
            {
                MyCubeBlockDefinition.MountPoint a = mounts[i];
                if (!a.Enabled)
                {
                    continue;
                }

                Vector3 min = Vector3.Min(a.Start, a.End);
                Vector3 max = Vector3.Max(a.Start, a.End);
                min -= (Vector3)p;
                max -= (Vector3)p;
                BoundingBox mainBox = new BoundingBox(min, max);
                for (int j = 0; j < neighborMounts.Count; j++)
                {
                    MyCubeBlockDefinition.MountPoint b = neighborMounts[j];
                    if (!b.Enabled)
                    {
                        continue;
                    }

                    BoundingBox neighborBox = new BoundingBox(Vector3.Min(b.Start, b.End), Vector3.Max(b.Start, b.End));

                    if (mainBox.Intersects(neighborBox))
                    {
                        BoundingBox box = mainBox.Intersect(neighborBox);

                        Vector3I va = new Vector3I((int)Math.Round(box.Max.X - box.Min.X), (int)Math.Round(box.Max.Y - box.Min.Y), (int)Math.Round(box.Max.Z - box.Min.Z));

                        int area = (va.X == 0) ? 1 : va.X;
                        area *= (va.Y == 0) ? 1 : va.Y;
                        area *= (va.Z == 0) ? 1 : va.Z;

                        totalArea += area;
                    }
                }
            }

            return totalArea;
        }

        protected void CalculatekA()
        {
            kA = new float[Neighbors.Count];
            for (int i = 0; i < Neighbors.Count; i++)
            {
                float area = Math.Min(Area, Neighbors[i].Area);
                kA[i] = Definition.Conductivity * area * TouchingSerfacesByNeighbor[i];
            }
        }

        protected void RemoveNeighbor(ThermalCell n2)
        {
            int i = Neighbors.IndexOf(n2);
            if (i != -1)
            {
                Neighbors.RemoveAt(i);
                TouchingSerfacesByNeighbor.RemoveAt(i);
                CalculatekA();
            }

            int j = n2.Neighbors.IndexOf(this);
            if (i != -1)
            {
                n2.Neighbors.RemoveAt(j);
                n2.TouchingSerfacesByNeighbor.RemoveAt(j);
                n2.CalculatekA();
            }
        }

        public float GetTemperature()
        {
            return Temperature;
        }

        private void UpdateHeat()
        {
            // power produced and consumed are in Watts or Joules per second.
            // it gets multiplied by the waste energy percent and timescale ratio.
            // we then have the heat in joules that needs to be converted into temprature.
            // we do that by dividing it by the ThermalMass (SpecificHeat * Mass)


            float produced = EnergyProduction * Definition.ProducerWasteEnergy;
            float consumed = (EnergyConsumption + ThrustEnergyConsumption) * Definition.ConsumerWasteEnergy;
            HeatGeneration = Settings.Instance.TimeScaleRatio * (produced + consumed) * ThermalMassInv;
        }
        /// <summary>
        /// Update the temperature of each cell in the grid
        /// </summary>
        internal void Update()
        {
            // update frame states
            Frame = Grid.SimulationFrame;
            LastTemprature = Temperature;

            float totalRadiation = CalculateTotalRadiation();

            float deltaTemperature = 0f;
            for (int i = 0; i < Neighbors.Count; i++)
            {
                float neighborTemp = Neighbors[i].Frame == Frame ? Neighbors[i].LastTemprature : Neighbors[i].Temperature;
                deltaTemperature += kA[i] * (neighborTemp - Temperature);
            }

            DeltaTemperature = ((C * deltaTemperature) + (totalRadiation * ThermalMassInv)) * Settings.Instance.TimeScaleRatio;


            Temperature = Math.Max(0, Temperature + DeltaTemperature);

            // update heat generation
            HeatGeneration = Settings.Instance.TimeScaleRatio * ((EnergyProduction * Definition.ProducerWasteEnergy) + ((EnergyConsumption + ThrustEnergyConsumption) * Definition.ConsumerWasteEnergy)) * ThermalMassInv;
            Temperature += HeatGeneration;


            //TODO: this is only used for debug and can be removed later on
            //Radiation = C * totalRadiation * ThermalMassInv;

            HandleCriticalTemperature();
            DebugDrawColors();
        }

        private float CalculateTotalRadiation()
        {
            float totalRadiation = 0;

            if (Settings.Instance.EnableEnvironment)
            {
                float temperatureSquared = Temperature * Temperature;
                totalRadiation = Boltzmann * Definition.Emissivity * (temperatureSquared * temperatureSquared) - Grid.FrameAmbientTempratureP4;
            }

            if (Settings.Instance.EnableSolarHeat && !Grid.FrameSolarOccluded)
            {
                float intensity = DirectionalRadiationIntensity(ref Grid.FrameSolarDirection, ref Grid.SolarRadiationNode);
                totalRadiation += Settings.Instance.SolarEnergy * Definition.Emissivity * (intensity * ExposedSurfaceArea);
            }

            return totalRadiation;
        }



        private void HandleCriticalTemperature()
        {
            if (Settings.Instance.EnableDamage && Temperature > Definition.CriticalTemperature)
            {
                Block.DoDamage((Temperature - Definition.CriticalTemperature) * Definition.CriticalTemperatureScaler, MyStringHash.GetOrCompute("thermal"), false);
            }
        }

        public void UpdateSurfaces()
        {
            ExposedSurfacesByDirection = Grid.GetExposedSurfacesByDirection(Block.Min, Block.Max+1);

            ExposedSurfaces = ExposedSurfacesByDirection[0] + ExposedSurfacesByDirection[1] + ExposedSurfacesByDirection[2] +
                ExposedSurfacesByDirection[3] + ExposedSurfacesByDirection[4] + ExposedSurfacesByDirection[5];

            UpdateExposedSurfaceArea();
        }

        private void UpdateExposedSurfaceArea()
        {
            ExposedSurfaceArea = ExposedSurfaces * Area;
            Boltzmann = -1 * Definition.Emissivity * Tools.BoltzmannConstant * ExposedSurfaceArea;
        }

        internal float DirectionalRadiationIntensity(ref Vector3 targetDirection, ref ThermalRadiationNode node)
        {
            float intensity = 0;
            bool isCube = (Block.Max - Block.Min).Volume() <= 1;

            for (int i = 0; i < 6; i++)
            {
                intensity += CalculateDirectionIntensity(i, ExposedSurfacesByDirection[i], ref targetDirection, ref node, isCube);
            }

            return intensity;
        }

        private float CalculateDirectionIntensity(int directionIndex, int surfaceCount, ref Vector3 targetDirection, ref ThermalRadiationNode node, bool isCube)
        {
            Vector3I direction = ThermalGrid.Directions[directionIndex];
            Vector3D startDirection = Vector3D.Rotate(direction, Grid.FrameMatrix);
            float dot = Vector3.Dot(startDirection, targetDirection);

            dot = Math.Max(0, dot);

            if (isCube)
            {
                node.Sides[directionIndex] += dot * surfaceCount;
                node.SideSurfaces[directionIndex] += surfaceCount;
                return dot;
            }
            else
            {
                return Math.Min(dot, node.SideAverages[directionIndex]);
            }
        }

        private void DebugDrawColors()
        {
            if (Settings.Instance.DebugBlockColors && MyAPIGateway.Session.IsServer)
            {
                Vector3 color = Tools.GetTemperatureColor(Temperature);
                if (Block.ColorMaskHSV != color)
                {
                    Block.CubeGrid.ColorBlocks(Block.Min, Block.Max, color);
                }
            }
        }

    }
}
