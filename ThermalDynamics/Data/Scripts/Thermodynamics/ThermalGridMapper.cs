using ProtoBuf.Meta;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using System.Diagnostics;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using System.Linq;
using System.Runtime.CompilerServices;
using VRageRender;

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {
        private Vector3I min;
        private Vector3I max;

        /// <summary>
        /// Room id 0 and 1 are reserved for external nodes and airtight nodes respectively
        /// </summary>
        public int CurrentRoomId = 1;
        public int NodeCountPerFrame = 1;
        public bool NodeUpdateComplete = false;

        public Queue<Vector3I> ExteriorQueue = new Queue<Vector3I>();
        public Queue<Vector3I> SolidQueue = new Queue<Vector3I>();
        public Queue<Vector3I> RoomQueue = new Queue<Vector3I>();

        public Dictionary<Vector3I, int> NodeSurfaces = new Dictionary<Vector3I, int>();

        public HashSet<Vector3I> ExteriorNodes = new HashSet<Vector3I>();
        public HashSet<Vector3I> SolidNodes = new HashSet<Vector3I>();
        //public HashSet<Vector3I> InteriorNodes = new HashSet<Vector3I>();

        public Dictionary<Vector3I, int> Rooms = new Dictionary<Vector3I, int>();

        private void AddBlockMapping(ref IMySlimBlock block)
        {
            ResetSpacialMapping();

            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;
            Vector3I[] neighbors = Base6Directions.IntDirections;

            Queue<Vector3I> processQueue = new Queue<Vector3I>();

            MyCubeBlockDefinition def = block.BlockDefinition as MyCubeBlockDefinition;
            bool isAirtight = def?.IsAirTight == true;
            Matrix matrix;
            block.Orientation.GetMatrix(out matrix);
            matrix.TransposeRotationInPlace();
            MyCubeBlockDefinition.MountPoint[] mountPoints = def.MountPoints;

            MyLog.Default.Info($"[{Settings.Name}] Block Start");

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        int state = 0;
                        Vector3I node = new Vector3I(x, y, z);
                        
                        if (isAirtight)
                        {
                            state = 4032;
                        }
                        
                        for (int i = 0; i < neighbors.Length; i++)
                        {
                            Vector3I n = node + neighbors[i];
                            Vector3I towardSelf = node - n;

                            if (IsAirtight(ref block, ref def, ref node, ref towardSelf, ref matrix))
                            {
                                state |= 1 << i + 6;
                            }

                            int ns;
                            if (NodeSurfaces.TryGetValue(n, out ns))
                            {
                                state |= ((ns & 1 << i + 6) >> 6);
                                processQueue.Enqueue(n);
                            }
                        }

                        NodeSurfaces.Add(node, state);
                    }
                }
            }

            while (processQueue.Count != 0) 
            {
                UpdateNodeMapping(processQueue.Dequeue());
            }
        }

        public void UpdateBlockMapping(ref IMySlimBlock block)
        {
            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;
            Vector3I[] neighbors = Base6Directions.IntDirections;

            Queue<Vector3I> processQueue = new Queue<Vector3I>();

            MyCubeBlockDefinition def = block.BlockDefinition as MyCubeBlockDefinition;
            bool isAirtight = def?.IsAirTight == true;
            Matrix matrix;
            block.Orientation.GetMatrix(out matrix);
            matrix.TransposeRotationInPlace();

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        int state = 0;
                        Vector3I node = new Vector3I(x, y, z);

                        if (isAirtight)
                        {
                            state = 4032;
                        }

                        for (int i = 0; i < neighbors.Length; i++)
                        {
                            Vector3I n = node + neighbors[i];
                            Vector3I towardSelf = node - n;

                            if (IsAirtight(ref block, ref def, ref node, ref towardSelf, ref matrix))
                            {
                                state |= 1 << i + 6;
                            }

                            int ns;
                            if (NodeSurfaces.TryGetValue(n, out ns))
                            {
                                state |= ((ns & 1 << i + 6) >> 6);
                                processQueue.Enqueue(n);
                            }
                        }

                        if (NodeSurfaces[node] != state)
                        {
                            ResetSpacialMapping();
                            NodeSurfaces[node] = state;
                        }
                    }
                }
            }

            while (processQueue.Count != 0)
            {
                UpdateNodeMapping(processQueue.Dequeue());
            }
        }

        private void RemoveBlockMapping(ref IMySlimBlock block)
        {
            ResetSpacialMapping();

            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;
            Vector3I[] neighbors = Base6Directions.IntDirections;

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        Vector3I node = new Vector3I(x, y, z);
                        NodeSurfaces.Remove(node);

                        for (int i = 0; i < neighbors.Length; i++)
                        {
                            Vector3I n = node + neighbors[i];

                            if (!(n.X >= min.X && n.Y >= min.Y && n.Z >= min.Z &&
                                    n.X < max.X && n.Y < max.Y && n.Z < max.Z) &&
                                    NodeSurfaces.ContainsKey(n))
                            {
                                int oppositeFace = (i % 2 == 0) ? i+1 : i-1; 
                                NodeSurfaces[n] &= ~(1 << oppositeFace);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateNodeMapping(Vector3I node)
        {
            Vector3I[] neighbors = Base6Directions.IntDirections;

            int state = NodeSurfaces[node];
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector3I n = node + neighbors[i];

                int ns;
                NodeSurfaces.TryGetValue(n, out ns);
                state |= ((ns & 1 << i + 6) >> 6);

            }
            NodeSurfaces[node] = state;
        }

        private bool IsAirtight(ref IMySlimBlock block, ref MyCubeBlockDefinition def, ref Vector3I pos, ref Vector3I normal, ref Matrix matrix)
        {
            if (def.IsAirTight == true) return true;

            Vector3 position = Vector3.Zero;
            if (block.FatBlock != null)
            {
                position = pos - block.FatBlock.Position;
            }

            IMyDoor door = block.FatBlock as IMyDoor;
            bool isDoorClosed = door != null && (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing);

            Vector3 value = Vector3.Transform(position, matrix) + def.Center;
            Vector3I roundedValue = Vector3I.Round(value);

            if (door != null)
            {
                switch (def.IsCubePressurized[roundedValue][normal])
                {
                    case MyCubeBlockDefinition.MyCubePressurizationMark.NotPressurized:
                        return IsDoorAirtight(ref door, ref normal, ref def);
                    case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways:
                        return true;
                    case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed:
                        return isDoorClosed;
                }
            }
            else 
            {
                switch (def.IsCubePressurized[roundedValue][normal])
                {
                    case MyCubeBlockDefinition.MyCubePressurizationMark.NotPressurized:
                        return false;
                    case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways:
                        return true;
                }
            }


            // Default to not airtight if the key is not found or if no specific condition matches
            //return isDoorClosed && door != null && IsDoorAirtight(ref door, ref transformedNormal, ref def);
            return false;
        }

        private bool IsDoorAirtight(ref IMyDoor door, ref Vector3I normal, ref MyCubeBlockDefinition def)
        {
            if (!door.IsFullyClosed)
                return false;

            if (door is MyAirtightSlideDoor)
            {
                if (normal == Vector3I.Forward)
                {
                    return true;
                }
            }
            else if (door is MyAirtightDoorGeneric)
            {
                if (normal == Vector3I.Forward || normal == Vector3I.Backward)
                {
                    return true;
                }
            }

            // standard and advanced doors
            MyCubeBlockDefinition.MountPoint[] mountPoints = def.MountPoints;
            for (int i = 0; i < mountPoints.Length; i++)
            {
                //MyCubeBlockDefinition.MountPoint mountPoint = mountPoints[i];
                if (normal == mountPoints[i].Normal)
                {
                    return false;
                }
            }

            return true;
        }

        public void ResetSpacialMapping()
        {
            min = Grid.Min - 1;
            max = Grid.Max + 1;

            CurrentRoomId = 0;

            ExteriorQueue.Clear();
            ExteriorQueue.Enqueue(min);
            ExteriorNodes.Clear();

            SolidQueue.Clear();
            SolidNodes.Clear();

            RoomQueue.Clear();
            Rooms.Clear();

            NodeCountPerFrame = Math.Max((int)((max - min).Size / 60f), 1);
            NodeUpdateComplete = false;
        }

        public void MapSurfaces()
        {
            int loopCount = 0;
            if (ExteriorQueue.Count > 0)
            {
                CrawlOutside(ref loopCount);
            }
            else if (RoomQueue.Count > 0 || SolidQueue.Count > 0)
            {
                CrawlInside(ref loopCount);
            }
            else 
            {
                NodeUpdateComplete = true;
                ThermalCellUpdateComplete = false;
            }

        }

        private void CrawlOutside(ref int loopCount)
        {
            Vector3I[] neighbors = Base6Directions.IntDirections;

            while (ExteriorQueue.Count > 0 && loopCount < NodeCountPerFrame)
            {
                loopCount++;
                Vector3I node = ExteriorQueue.Dequeue();

                for (int i = 0; i < 6; i++)
                {
                    // get the neighbor location
                    Vector3I n = node + neighbors[i];

                    // skip if the neighbor is out of bounds or already checked.
                    if (ExteriorNodes.Contains(n) || Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max)
                        continue;

                    int flag;
                    NodeSurfaces.TryGetValue(n, out flag);

                    int shift = i + 6 + ((i % 2 == 0) ? 1 : -1);

                    // do not queue this node if it is airtight
                    if ((flag & 1 << shift) != 0) 
                    {
                        //make sure something is queued for finding internal rooms
                        //if (SolidQueue.Count == 0)
                        //{
                        //    SolidQueue.Enqueue(n);
                        //    SolidNodes.Add(n);
                        //}

                        continue;
                    }

                    // enqueue empty or non airtight nodes
                    ExteriorQueue.Enqueue(n);
                    ExteriorNodes.Add(n);
                }
            }
        }

        private void CrawlInside(ref int loopCount)
        {
            Vector3I[] neighbors = Base6Directions.IntDirections;
            // crawl over the current room
            while (RoomQueue.Count > 0 && loopCount < NodeCountPerFrame)
            {
                loopCount++;

                Vector3I node = RoomQueue.Dequeue();

                int flag;
                NodeSurfaces.TryGetValue(node, out flag);

                for (int i = 0; i < 6; i++)
                {
                    Vector3I n = node + neighbors[i];
                    if (Rooms.ContainsKey(n)) continue;

                    if ((flag & 1 << i) == 0) 
                    {
                        RoomQueue.Enqueue(n);
                        Rooms.Add(n, CurrentRoomId);
                    }
                }
            }

            // find a new room to crawl over
            while (RoomQueue.Count == 0 && SolidQueue.Count > 0 && loopCount < NodeCountPerFrame)
            {
                loopCount++;

                Vector3I node = SolidQueue.Dequeue();
                int surfaces = NodeSurfaces[node];

                for (int i = 0; i < neighbors.Length; i++)
                {
                    Vector3I n = node + neighbors[i];
                    if (ExteriorNodes.Contains(n) || SolidNodes.Contains(n) || Rooms.ContainsKey(n)) continue;

                    if ((surfaces & 1 << i) == 0)
                    {
                        RoomQueue.Enqueue(n);
                        Rooms.Add(n, CurrentRoomId++);
                        SolidQueue.Enqueue(node);
                        break;
                    }
                    else
                    {
                        SolidQueue.Enqueue(n);
                    }
                }
            }

            // keep calling this function if there is more work to do
            if (loopCount < NodeCountPerFrame && SolidQueue.Count > 0)
            {
                CrawlInside(ref loopCount);
            }
        }
    }
}