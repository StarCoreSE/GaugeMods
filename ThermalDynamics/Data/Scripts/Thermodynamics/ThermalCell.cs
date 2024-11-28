using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.GameServices;
using VRage.Utils;
using SpaceEngineers.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.Game;
using VRage.ObjectBuilders;
using System.IO.Compression;
using VRage.Game.Components.Interfaces;
using System.Drawing;
using System.Security.AccessControl;
using System.Net;
using VRageRender.Messages;
using static VRage.Game.MyObjectBuilder_CurveDefinition;


namespace Thermodynamics {
    public class ThermalCell {
        public int Id;
        public long Frame;

        public float Temperature;
        public float LastTemprature;
        public float DeltaTemperature;

        public float EnergyProduction;
        public float EnergyConsumption;
        public float ThrustEnergyConsumption;
        public float HeatGeneration;

        public float C;// c =  Temp / (watt * meter)
        public float Mass;// kg
        public float Area;// m^2
        public float ExposedSurfaceArea;// m^2 of all exposed faces on this block
        public float Radiation;
        public float ThermalMassInv;// 1 / SpecificHeat * Mass
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
        private Queue<Action> pendingListenerActions = new Queue<Action>();
        public ThermalCell(ThermalGrid g, IMySlimBlock b)
        {
            Grid = g;
            Block = b;
            Id = b.Position.Flatten();
            Definition = ThermalCellDefinition.GetDefinition(Block.BlockDefinition.Id);

            SetupListeners();// listeners now queue changes

            Mass = Block.Mass;
            Area = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;
            C = 1 / (Definition.SpecificHeat * Mass * Block.CubeGrid.GridSize);
            ThermalMassInv = 1f / (Definition.SpecificHeat * Mass);
            Boltzmann = -1 * Definition.Emissivity * Tools.BoltzmannConstant;

            UpdateHeat();
        }
        public float PreviousTemperature { get; set; }


        private void SetupListeners()
        {
            if (Block.FatBlock == null) return;

            IMyCubeBlock fat = Block.FatBlock;

            if (fat is IMyThrust)
            {
                IMyThrust thrust = (fat as IMyThrust);
                thrust.ThrustChanged += (block, old, current) =>
                {
                    pendingListenerActions.Enqueue(() => OnThrustChanged(block, old, current));
                };
                OnThrustChanged(thrust, 0, thrust.CurrentThrust);
            }
            else
            {
                fat.Components.ComponentAdded += (type, component) =>
                {
                    pendingListenerActions.Enqueue(() => OnComponentAdded(type, component));
                };
                fat.Components.ComponentRemoved += (type, component) =>
                {
                    pendingListenerActions.Enqueue(() => OnComponentRemoved(type, component));
                };

                if (fat.Components.Contains(typeof(MyResourceSourceComponent)))
                {
                    fat.Components.Get<MyResourceSourceComponent>().OutputChanged += (resourceType, oldOutput, source) =>
                    {
                        pendingListenerActions.Enqueue(() => PowerProducedChanged(resourceType, oldOutput, source));
                    };
                }

                if (fat.Components.Contains(typeof(MyResourceSinkComponent)))
                {
                    fat.Components.Get<MyResourceSinkComponent>().CurrentInputChanged += (resourceType, oldInput, sink) =>
                    {
                        pendingListenerActions.Enqueue(() => PowerConsumedChanged(resourceType, oldInput, sink));
                    };
                }
            }

            IMyPistonBase pistonBase = fat as IMyPistonBase;
            if (pistonBase != null)
            {
                pistonBase.AttachedEntityChanged += HandleAttachedEntityChanged;
            }
            else
            {
                IMyMotorBase motorBase = fat as IMyMotorBase;
                if (motorBase != null)
                {
                    motorBase.AttachedEntityChanged += HandleAttachedEntityChanged;
                }
                else
                {
                    IMyDoor door = fat as IMyDoor;
                    if (door != null)
                    {
                        door.DoorStateChanged += (state) =>
                        {
                            pendingListenerActions.Enqueue(() => Grid.UpdateBlockMapping(ref Block));
                        };
                    }
                    else
                    {
                        IMyLandingGear gear = fat as IMyLandingGear;
                        if (gear != null)
                        {
                            gear.StateChanged += (state) =>
                            {
                                pendingListenerActions.Enqueue(() =>
                                {
                                    IMyEntity entity = gear.GetAttachedEntity();
                                    ThermalCell c = Grid.Get(gear.Position);

                                    if (!(entity is MyCubeGrid))
                                    {
                                        c.AddAllNeighbors();
                                        return;
                                    }

                                    MyCubeGrid grid = entity as MyCubeGrid;
                                    ThermalGrid gtherms = grid.GameLogic.GetAs<ThermalGrid>();

                                    Vector3D oldMin = gear.CubeGrid.GridIntegerToWorld(new Vector3I(gear.Min.X, gear.Min.Y, gear.Min.Z));
                                    Vector3D oldMax = gear.CubeGrid.GridIntegerToWorld(new Vector3I(gear.Max.X, gear.Max.Y, gear.Min.Z));

                                    oldMax += gear.WorldMatrix.Down * (grid.GridSize + 0.2f);

                                    Vector3I min = grid.WorldToGridInteger(oldMin);
                                    Vector3I max = grid.WorldToGridInteger(oldMax);

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
                                                if (ncell != null)
                                                {
                                                    c.AddNeighbor(ncell);
                                                }
                                            }
                                        }
                                    }

                                    c.CalculatekA();
                                });
                            };
                        }
                    }
                }
            }
        }

        private void HandleAttachedEntityChanged(IMyMechanicalConnectionBlock block)
        {
            pendingListenerActions.Enqueue(() => GridGroupChanged(block));
        }

        private void OnComponentAdded(Type compType, IMyEntityComponentBase component)
        {
            if (compType == typeof(MyResourceSourceComponent))
            {
                ((MyResourceSourceComponent)component).OutputChanged += PowerProducedChanged;
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
                if (resourceTypeId != MyResourceDistributorComponent.ElectricityId) return;
                EnergyConsumption = sink.CurrentInputByType(resourceTypeId) * Tools.MWtoWatt;
                UpdateHeat();
            }
            catch
            {
                // ignored
            }
        }

        private void PowerProducedChanged(MyDefinitionId resourceTypeId, float oldOutput, MyResourceSourceComponent source)
        {
            try
            {
                if (resourceTypeId != MyResourceDistributorComponent.ElectricityId) return;
                EnergyProduction = source.CurrentOutputByType(resourceTypeId) * Tools.MWtoWatt;
                UpdateHeat();
            }
            catch
            {
                // ignored
            }
        }

        public void ClearNeighbors()
        {
            for (int i = 0; i < Neighbors.Count; i++)
            {
                ThermalCell ncell = Neighbors[i];
                int j = ncell.Neighbors.IndexOf(this);
                if (j == -1) continue;
                ncell.Neighbors.RemoveAt(j);
                ncell.TouchingSerfacesByNeighbor.RemoveAt(j);
                ncell.CalculatekA();
            }

            Neighbors.Clear();
            TouchingSerfacesByNeighbor.Clear();
            CalculatekA();
        }

        public void AddAllNeighbors()
        {
            ClearNeighbors();
            List<IMySlimBlock> neighbors = new List<IMySlimBlock>();
            Block.GetNeighbours(neighbors);

            for (int i = 0; i < neighbors.Count; i++)
            {
                IMySlimBlock n = neighbors[i];
                if (!Contains(n.Position)) continue;// Use Contains for boundary check
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

                    if (!mainBox.Intersects(neighborBox)) continue;
                    BoundingBox box = mainBox.Intersect(neighborBox);

                    Vector3I va = new Vector3I((int)Math.Round(box.Max.X - box.Min.X), (int)Math.Round(box.Max.Y - box.Min.Y), (int)Math.Round(box.Max.Z - box.Min.Z));

                    int area = (va.X == 0) ? 1 : va.X;
                    area *= (va.Y == 0) ? 1 : va.Y;
                    area *= (va.Z == 0) ? 1 : va.Z;

                    totalArea += area;
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
            if (i == -1) return;
            n2.Neighbors.RemoveAt(j);
            n2.TouchingSerfacesByNeighbor.RemoveAt(j);
            n2.CalculatekA();
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
            try
            {
                // Process any pending listener actions
                while(pendingListenerActions.Count > 0)
                {
                    try
                    {
                        Action action = pendingListenerActions.Dequeue();
                        action.Invoke();
                    }
                    catch (Exception ex)
                    {
                        MyLog.Default.Error($"Error processing thermal listener action: {ex}");
                    }
                }

                UpdateFrameAndLastTemperature();
                float totalRadiation = CalculateTotalRadiation();
                float deltaTemperature = CalculateDeltaTemperature();
                ApplyTemperatureChanges(totalRadiation, deltaTemperature);
                UpdateHeatGeneration();
                ApplyHeatGeneration();
                HandleCriticalTemperature();
                UpdateDebugVisuals();
            }
            catch (Exception ex)
            {
                MyLog.Default.Error($"Error in thermal cell update: {ex}");
            }
        }

        private void UpdateFrameAndLastTemperature()
        {
            Frame = Grid.SimulationFrame;
            LastTemprature = Temperature;
        }

        private float CalculateTotalRadiation()
        {
            float temperatureSquared = Temperature * Temperature;
            float totalRadiation = Boltzmann * Definition.Emissivity * (temperatureSquared * temperatureSquared) - Grid.FrameAmbientTempratureP4;

            if (!Settings.Instance.EnableSolarHeat || Grid.FrameSolarOccluded) return totalRadiation;
            float intensity = DirectionalRadiationIntensity(ref Grid.FrameSolarDirection, ref Grid.SolarRadiationNode);
            totalRadiation += Settings.Instance.SolarEnergy * Definition.Emissivity * (intensity * ExposedSurfaceArea);

            return totalRadiation;
        }

        private float CalculateDeltaTemperature()
        {
            float deltaTemperature = 0f;
            float currentTemperature = Temperature;
            for (int i = 0; i < Neighbors.Count; i++)
            {
                float neighborTemp = Neighbors[i].Frame == Frame ? Neighbors[i].LastTemprature : Neighbors[i].Temperature;
                deltaTemperature += kA[i] * (neighborTemp - currentTemperature);
            }


            return deltaTemperature;
        }

        private void ApplyTemperatureChanges(float totalRadiation, float deltaTemperature)
        {
            DeltaTemperature = (C * deltaTemperature + totalRadiation * ThermalMassInv) * Settings.Instance.TimeScaleRatio;
            Temperature = Math.Max(0, Temperature + DeltaTemperature);
        }

        private void UpdateHeatGeneration()
        {
            HeatGeneration = Settings.Instance.TimeScaleRatio * ((EnergyProduction * Definition.ProducerWasteEnergy) + ((EnergyConsumption + ThrustEnergyConsumption) * Definition.ConsumerWasteEnergy)) * ThermalMassInv;
        }

        private void ApplyHeatGeneration()
        {
            Temperature += HeatGeneration;
        }

        private void HandleCriticalTemperature()
        {
            if (Settings.Instance.EnableDamage && Temperature > Definition.CriticalTemperature)
            {
                Block.DoDamage((Temperature - Definition.CriticalTemperature) * Definition.CriticalTemperatureScaler, MyStringHash.GetOrCompute("thermal"), false);
            }
        }

        private void UpdateDebugVisuals()
        {
            if (!Settings.DebugBlockColors || !MyAPIGateway.Session.IsServer) return;
            Vector3 color = Tools.GetTemperatureColor(Temperature);
            if (Block.ColorMaskHSV != color)
            {
                Block.CubeGrid.ColorBlocks(Block.Min, Block.Max, color);
            }
        }


        private void ResetExposedSurfaces()
        {
            //MyLog.Default.Info($"[Thermals] Reset Exposed Surfaces");
            ExposedSurfaces = 0;
            ExposedSurfacesByDirection = new int[6];
        }

        private Dictionary<Vector3I, float> internalRoomTemperatures = new Dictionary<Vector3I, float>();
        private Dictionary<Vector3I, RoomData> internalRooms = new Dictionary<Vector3I, RoomData>();
        private const float ROOM_TEMPERATURE_EXCHANGE_RATE = 0.1f;
        private const float ROOM_HEAT_CAPACITY = 1005f;// Approximate heat capacity of air (J/kg·K)
        private const float ROOM_AIR_DENSITY = 1.225f;// Approximate density of air at room temperature (kg/m³)

        private struct RoomData {
            public float Temperature;
            public float Volume;
            public float ThermalMass;
            public HashSet<Vector3I> Nodes;

            public RoomData(float temp, float vol, HashSet<Vector3I> nodes)
            {
                Temperature = temp;
                Volume = vol;
                Nodes = nodes;
                ThermalMass = ROOM_HEAT_CAPACITY * ROOM_AIR_DENSITY * Volume;
            }
        }

        private void HandleInternalRooms(Vector3I nodeNei)
        {
            if (!internalRooms.ContainsKey(nodeNei))
            {
                // Find all connected internal nodes and create a new room
                HashSet<Vector3I> roomNodes = new HashSet<Vector3I>();
                FindConnectedInternalNodes(nodeNei, roomNodes);

                float roomVolume = roomNodes.Count * Block.CubeGrid.GridSize * Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;
                float averageTemp = CalculateRoomAverageTemperature(roomNodes);

                internalRooms[nodeNei] = new RoomData(averageTemp, roomVolume, roomNodes);
            }

            RoomData roomData = internalRooms[nodeNei];

            // Calculate heat exchange
            float surfaceArea = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;// Area of one face
            float heatTransferCoefficient = 5.0f;// Approximate value for natural convection

            float heatTransferred = heatTransferCoefficient * surfaceArea * (Temperature - roomData.Temperature) * Settings.Instance.TimeScaleRatio;

            // Update block temperature
            float blockTemperatureChange = heatTransferred / (Definition.SpecificHeat * Mass);
            Temperature -= blockTemperatureChange;

            // Update room temperature
            float roomTemperatureChange = heatTransferred / roomData.ThermalMass;
            roomData.Temperature += roomTemperatureChange;

            // Update the room data in the dictionary
            internalRooms[nodeNei] = roomData;

            // Periodically equalize temperatures between connected rooms
            if (Grid.SimulationFrame % 60 == 0)// Adjust frequency as needed
            {
                EqualizeConnectedRooms(nodeNei);
            }
        }
        private void EqualizeConnectedRooms(Vector3I node)
        {
            RoomData currentRoom;
            if (!internalRooms.TryGetValue(node, out currentRoom)) return;

            HashSet<Vector3I> checkedNodes = new HashSet<Vector3I>();
            Queue<Vector3I> toCheck = new Queue<Vector3I>();
            toCheck.Enqueue(node);

            while(toCheck.Count > 0)
            {
                Vector3I currentNode = toCheck.Dequeue();
                if (!checkedNodes.Add(currentNode)) continue;

                foreach (Vector3I direction in Base6Directions.IntDirections)
                {
                    Vector3I neighborNode = currentNode + direction;
                    RoomData neighborRoom;
                    if (!internalRooms.TryGetValue(neighborNode, out neighborRoom)) continue;

                    if (currentRoom.Temperature != neighborRoom.Temperature)
                    {
                        // Calculate equalized temperature based on thermal masses
                        float totalThermalMass = currentRoom.ThermalMass + neighborRoom.ThermalMass;
                        float equalizedTemp = (currentRoom.Temperature * currentRoom.ThermalMass +
                                               neighborRoom.Temperature * neighborRoom.ThermalMass) /
                                              totalThermalMass;

                        // Update temperatures
                        currentRoom.Temperature = equalizedTemp;
                        neighborRoom.Temperature = equalizedTemp;

                        internalRooms[node] = currentRoom;
                        internalRooms[neighborNode] = neighborRoom;
                    }

                    toCheck.Enqueue(neighborNode);
                }
            }
        }
        private void FindConnectedInternalNodes(Vector3I start, HashSet<Vector3I> roomNodes)
        {
            Queue<Vector3I> queue = new Queue<Vector3I>();
            queue.Enqueue(start);

            while(queue.Count > 0)
            {
                Vector3I current = queue.Dequeue();
                if (!roomNodes.Add(current)) continue;

                foreach (Vector3I direction in Base6Directions.IntDirections)
                {
                    Vector3I neighbor = current + direction;
                    if (Grid.Grid.CubeExists(neighbor) || Contains(neighbor)) continue;
                    if (Grid.ExteriorNodes.Contains(neighbor)) continue;// Skip if it's an exterior node

                    queue.Enqueue(neighbor);
                }
            }
        }

        private float CalculateRoomAverageTemperature(HashSet<Vector3I> roomNodes)
        {
            float totalTemp = 0;
            int contributingBlocks = 0;

            foreach (Vector3I node in roomNodes)
            {
                foreach (Vector3I direction in Base6Directions.IntDirections)
                {
                    Vector3I blockPos = node + direction;
                    if (Contains(blockPos)) continue;
                    ThermalCell cell = Grid.Get(blockPos);
                    if (cell == null) continue;
                    totalTemp += cell.Temperature;
                    contributingBlocks++;
                }
            }

            return contributingBlocks > 0 ? totalTemp / contributingBlocks : Grid.FrameAmbientTemprature;
        }

        public void UpdateSurfaces(ref HashSet<Vector3I> exterior, ref Dictionary<Vector3I, int> nodeSurfaces)
        {
            ResetExposedSurfaces();

            Vector3I min = Block.Min;
            Vector3I size = Block.Max - min + Vector3I.One;
            int xMask = (1 << size.X) - 1;
            int yMask = (1 << size.Y) - 1;
            int zMask = (1 << size.Z) - 1;

            // Pre-shift masks to match block position
            int xShiftedMask = xMask << min.X;
            int yShiftedMask = yMask << min.Y;
            int zShiftedMask = zMask << min.Z;

            for (int x = min.X; x < Block.Max.X + 1; x++)
            {
                if ((1 << x & xShiftedMask) == 0) continue;

                for (int y = min.Y; y < Block.Max.Y + 1; y++)
                {
                    if ((1 << y & yShiftedMask) == 0) continue;

                    for (int z = min.Z; z < Block.Max.Z + 1; z++)
                    {
                        if ((1 << z & zShiftedMask) == 0) continue;

                        Vector3I node = new Vector3I(x, y, z);
                        int surfaceBits = nodeSurfaces[node];

                        for (int i = 0; i < 6; i++)
                        {
                            if ((surfaceBits & (1 << i)) != 0) continue;

                            Vector3I nodeNei = node + Base6Directions.IntDirections[i];

                            if (exterior.Contains(nodeNei))
                            {
                                ExposedSurfaces++;
                                ExposedSurfacesByDirection[i]++;
                            }
                            else
                            {
                                HandleInternalRooms(nodeNei);
                            }
                        }
                    }
                }
            }

            UpdateExposedSurfaceArea();
        }


        public bool Contains(Vector3I n)
        {
            Vector3I relativePos = n - Block.Min;
            Vector3I size = Block.Max - Block.Min + Vector3I.One;

            return (relativePos.X & ~(size.X - 1)) == 0 &&
                   (relativePos.Y & ~(size.Y - 1)) == 0 &&
                   (relativePos.Z & ~(size.Z - 1)) == 0;
        }


        private void UpdateExposedSurfaceArea()
        {
            ExposedSurfaceArea = ExposedSurfaces * Area;
            Boltzmann = -1 * Definition.Emissivity * Tools.BoltzmannConstant * ExposedSurfaceArea;
        }

        internal float DirectionalRadiationIntensity(ref Vector3 targetDirection, ref ThermalRadiationNode node)
        {
            float intensity = 0;
            MatrixD matrix = Grid.FrameMatrix;
            bool isCube = (Block.Max - Block.Min).Volume() <= 1;

            for (int i = 0; i < ExposedSurfacesByDirection.Length; i++)
            {
                intensity += CalculateDirectionIntensity(i, ExposedSurfacesByDirection[i], ref targetDirection, ref node, matrix, isCube);
            }

            return intensity;
        }

        private float CalculateDirectionIntensity(int directionIndex, int surfaceCount, ref Vector3 targetDirection, ref ThermalRadiationNode node, MatrixD matrix, bool isCube)
        {
            Vector3I direction = Base6Directions.IntDirections[directionIndex];
            Vector3D startDirection = Vector3D.Rotate(direction, matrix);
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

    }
}