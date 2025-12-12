using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Utils;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using VRageMath;

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {
        /// <summary>
        /// Bit flags representing surface states for each face of a block and its neighbors.
        /// Uses 30 bits total: 6 bits for self airtight, 6 for neighbor airtight, 6 for self mount points, 6 for neighbor mount points.
        /// Each group of 6 represents Forward, Left, Up, Down, Right, Backward directions in that order.
        /// </summary>
        public enum SurfaceFlags
        {
            // Offset values to position each flag group within the 30-bit integer
            NeighborAirtightOffset = 6,      // Neighbor airtight flags start at bit 6
            SelfMountPointOffset = 12,       // Self mount point flags start at bit 12
            NeighborMountPointOffset = 18,   // Neighbor mount point flags start at bit 18

            // Self airtight flags (bits 0-5): whether this block face is airtight from inside
            SelfAirtightForward = 1,         // Bit 0
            SelfAirtightLeft = 2,            // Bit 1
            SelfAirtightUp = 4,              // Bit 2
            SelfAirtightDown = 8,            // Bit 3
            SelfAirtightRight = 16,          // Bit 4
            SelfAirtightBackward = 32,       // Bit 5
            SelfAirtightFull = 63,           // All self airtight bits set (bits 0-5)

            // Neighbor airtight flags (bits 6-11): whether adjacent blocks are airtight toward this block
            NeighborAirtightForward = 64,    // Bit 6
            NeighborAirtightLeft = 128,      // Bit 7
            NeighborAirtightUp = 256,        // Bit 8
            NeighborAirtightDown = 512,      // Bit 9
            NeighborAirtightRight = 1024,    // Bit 10
            NeighborAirtightBackward = 2048, // Bit 11
            NeighborAirtightFull = 4032,     // All neighbor airtight bits set (bits 6-11)

            // Self mount point flags (bits 12-17): whether this block has mount points on each face
            SelfMountPointForward = 4096,    // Bit 12
            SelfMountPointLeft = 8192,       // Bit 13
            SelfMountPointUp = 16384,        // Bit 14
            SelfMountPointDown = 32768,      // Bit 15
            SelfMountPointRight = 65536,     // Bit 16
            SelfMountPointBackward = 131072, // Bit 17
            SelfMountPointFull = 258048,     // All self mount point bits set (bits 12-17)

            // Neighbor mount point flags (bits 18-23): whether adjacent blocks have mount points toward this block
            NeighborMountPointForward = 262144,    // Bit 18
            NeighborMountPointLeft = 524288,      // Bit 19
            NeighborMountPointUp = 1048576,       // Bit 20
            NeighborMountPointDown = 2097152,     // Bit 21
            NeighborMountPointRight = 4194304,    // Bit 22
            NeighborMountPointBackward = 8388608, // Bit 23
            NeighborMountPointFull = 16505688,    // All neighbor mount point bits set (bits 18-23)
        }

        public static Vector3I[] Directions = new Vector3I[] {
                Vector3I.Forward,
                Vector3I.Left,
                Vector3I.Up,
                Vector3I.Down,
                Vector3I.Right,
                Vector3I.Backward
            };

        public static BoundingBox[] mountBounds = new BoundingBox[]
        {
            new BoundingBox { Min = new Vector3(0.002f, 0.002f, -0.1f), Max = new Vector3(0.998f, 0.998f, 0.1f) },
            new BoundingBox { Min = new Vector3(-0.1f, 0.002f, 0.002f), Max = new Vector3(0.1f, 0.998f, 0.998f) },
            new BoundingBox { Min = new Vector3(0.002f, 0.900f, 0.002f), Max = new Vector3(0.998f, 1.1f, 0.998f) },
            new BoundingBox { Min = new Vector3(0.002f, -0.1f, 0.002f), Max = new Vector3(0.998f, 0.1f, 0.998f) },
            new BoundingBox { Min = new Vector3(0.900f, 0.002f, 0.002f), Max = new Vector3(1.1f, 0.998f, 0.998f) },
            new BoundingBox { Min = new Vector3(0.002f, 0.002f, 0.900f), Max = new Vector3(0.998f, 0.998f, 1.1f) },
        };

        /// <summary>
        /// A callback method that triggers when each process completes
        /// Systems that are dependant on this information should attach methods here
        /// </summary>
        public event Action SurfaceCheckComplete;

        /// <summary>
        /// The block surface info
        /// </summary>
        public Dictionary<Vector3I, int> Surfaces = new Dictionary<Vector3I, int>();
        /// <summary>
        /// the cells in the grid organized by rooms.
        /// index 0 are exterior cells
        /// index 1 are non room blocks
        /// </summary>
        public List<HashSet<Vector3I>> Rooms = new List<HashSet<Vector3I>>() { new HashSet<Vector3I>(), new HashSet<Vector3I>() };
        public List<HashSet<Vector3I>> RoomBuffer = new List<HashSet<Vector3I>>() { new HashSet<Vector3I>(), new HashSet<Vector3I>() };

        private int Quota;

        public Queue<Vector3I> ExternalQueue = new Queue<Vector3I>();
        public Queue<Vector3I> GridQueue = new Queue<Vector3I>();


        private Vector3I min;
        private Vector3I max;

        public void OnBlockAddOrUpdate(IMySlimBlock block)
        {
            //MyLog.Default.Info($"[{Settings.Name}] OnBlockAddOrUpdate: {block.BlockDefinition.Id} at {block.Position}");

            if (block.FatBlock is IMyDoor)
            {
                ((IMyDoor)block.FatBlock).DoorStateChanged += (state) => OnBlockAddOrUpdate(block);
            }

            CalculateBlockSurfaceStates(block);

            BeginCrawl();
        }

        private void OnBlockRemoved(IMySlimBlock block)
        {
            if (block.FatBlock is IMyDoor)
            {
                ((IMyDoor)block.FatBlock).DoorStateChanged -= (state) => OnBlockAddOrUpdate(block);
            }

            if (Grid.BlocksCount == 0) return;

            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        Vector3I node = new Vector3I(x, y, z);
                        Surfaces.Remove(node);

                        for (int i = 0; i < Directions.Length; i++)
                        {
                            Vector3I n = node + Directions[i];

                            if (!(n.X >= min.X && n.Y >= min.Y && n.Z >= min.Z &&
                                    n.X < max.X && n.Y < max.Y && n.Z < max.Z) &&
                                    Surfaces.ContainsKey(n))
                            {
                                int surface = Surfaces[n];
                                surface &= ~(1 << 5 - i + (int)SurfaceFlags.NeighborAirtightOffset);
                                surface &= ~(1 << 5 - i + (int)SurfaceFlags.NeighborMountPointOffset);
                                Surfaces[n] = surface;

                                //MyLog.Default.Info(DebugSurfaceStateText(surface));
                            }
                        }
                    }
                }
            }

            BeginCrawl();
        }


        /// <summary>
        /// Calculate complete surface states for a block and update neighbors.
        ///
        /// This method performs a complex analysis of block surfaces by:
        /// 1. Iterating through every cell within the block's bounding box
        /// 2. Determining airtightness based on block definition and door states
        /// 3. Checking mount points for connector compatibility
        /// 4. Updating neighbor blocks with reciprocal surface information
        ///
        /// The algorithm uses bit flags to efficiently store surface states in a compact 30-bit integer,
        /// representing airtightness and mount points for all 6 faces plus neighbor relationships.
        /// </summary>
        private void CalculateBlockSurfaceStates(IMySlimBlock block)
        {
            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;
            Vector3I position = block.Position;

            Queue<Vector3I> processQueue = new Queue<Vector3I>();

            List<MyCubeBlockDefinition.MountPoint> mounts = new List<MyCubeBlockDefinition.MountPoint>();
            MyCubeBlockDefinition def = block.BlockDefinition as MyCubeBlockDefinition;
            bool isAirtight = def?.IsAirTight == true;
            Matrix matrix;
            MyBlockOrientation orientation = block.Orientation;
            orientation.GetMatrix(out matrix);
            matrix.TransposeRotationInPlace();
            MyCubeGrid.TransformMountPoints(mounts, def, def.MountPoints, ref orientation);

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        int state = 0;
                        Vector3I cell = new Vector3I(x, y, z);
                        Vector3I local = cell - position;
                        Vector3I tcell = Vector3I.Round(Vector3.Transform(local, matrix) + def.Center);

                        //MyLog.Default.Info($"[CalculateBlockSurfaceStates] start {cell} - {DebugSurfaceStateText(state)}");


                        if (isAirtight)
                        {
                            state = (int)SurfaceFlags.SelfAirtightFull;
                        }

                        //MyLog.Default.Info($"[CalculateBlockSurfaceStates] isAirtight {cell} - {DebugSurfaceStateText(state)}");

                        for (int a = 0; a < Directions.Length; a++)
                        {
                            Vector3I dir = Directions[a];
                            Vector3I local_dir = Vector3I.Round(Vector3.Transform(dir, matrix));
                            Vector3I ncell = cell + dir;

                            if (!isAirtight)
                            {
                                MyCubeBlockDefinition.MyCubePressurizationMark mark = def.IsCubePressurized[tcell][local_dir];
                                if (mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways)
                                {
                                    if (block.FatBlock is IMyDoor)
                                    {
                                        //MyLog.Default.Info($"[PressurizedAlways] {Tools.IndexToDirectionName(a)}");
                                    }
                                    state |= 1 << a;
                                }
                                else if (block.FatBlock is IMyDoor)
                                {
                                    IMyDoor door = (IMyDoor)block.FatBlock;
                                    if (mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed)
                                    {
                                        if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing)
                                        {
                                            //MyLog.Default.Info($"[PressurizedClosed] {Tools.IndexToDirectionName(a)}");
                                            state |= 1 << a;
                                        }
                                    }
                                    else if (door.IsFullyClosed)
                                    {

                                        if (door is MyAirtightSlideDoor)
                                        {
                                            if (local_dir == Vector3I.Forward)
                                            {
                                                //MyLog.Default.Info($"[IsFullyClosed] MyAirtightSlideDoor {Tools.IndexToDirectionName(a)}");
                                                state |= 1 << a;
                                            }
                                        }
                                        else if (door is MyAirtightDoorGeneric)
                                        {
                                            if (local_dir == Vector3I.Forward || local_dir == Vector3I.Backward)
                                            {
                                                //MyLog.Default.Info($"[IsFullyClosed] MyAirtightDoorGeneric {Tools.IndexToDirectionName(a)}");
                                                state |= 1 << a;
                                            }
                                        }
                                        else
                                        {
                                            bool isValid = true;
                                            for (int i = 0; i < mounts.Count; i++)
                                            {
                                                if (dir == mounts[i].Normal)
                                                {
                                                    isValid = false;
                                                }
                                            }

                                            if (isValid)
                                            {
                                                //MyLog.Default.Info($"[IsFullyClosed] OtherDoors DirectionsWithoutMountPoints {Tools.IndexToDirectionName(a)}");
                                                state |= 1 << a;
                                            }
                                        }
                                    }
                                }
                            }

                            for (int b = 0; b < mounts.Count; b++)
                            {
                                MyCubeBlockDefinition.MountPoint m = mounts[b];
                                if (!m.Enabled || m.Normal != dir)
                                {
                                    continue;
                                }

                                Vector3 mmin = Vector3.Min(m.Start, m.End) - local;
                                Vector3 mmax = Vector3.Max(m.Start, m.End) - local;
                                BoundingBox mountBox = new BoundingBox(mmin, mmax);
                                BoundingBox cellbox = mountBounds[a];

                                if (cellbox.Intersects(mountBox))
                                {
                                    state |= 1 << a + (int)SurfaceFlags.SelfMountPointOffset;
                                }
                            }

                            int ns;
                            if (Surfaces.TryGetValue(ncell, out ns))
                            {
                                int bitp = 5 - a;
                                state |= (ns & 1 << bitp) >> bitp << a + (int)SurfaceFlags.NeighborAirtightOffset;

                                bitp = 5 - a + (int)SurfaceFlags.SelfMountPointOffset;
                                state |= (ns & 1 << bitp) >> bitp << a + (int)SurfaceFlags.NeighborMountPointOffset;

                                //MyLog.Default.Info($"[ncell] {ncell} --- {DebugSurfaceStateText(state)}");
                                processQueue.Enqueue(ncell);
                            }
                        }

                        MyLog.Default.Info($"[CalculateBlockSurfaceStates] {cell} - {DebugSurfaceStateText(state)}");

                        //MyLog.Default.Info(DebugSurfaceStateText(state));

                        if (!Surfaces.ContainsKey(cell))
                        {
                            Surfaces.Add(cell, state);
                        }
                        else
                        {
                            Surfaces[cell] = state;
                        }
                    }
                }
            }

            while (processQueue.Count != 0)
            {
                UpdateCell(processQueue.Dequeue());
            }
        }

        /// <summary>
        /// Updates neighbor surface information for a specific cell after its own surfaces have been calculated.
        ///
        /// This method performs reciprocal neighbor updates using complex bit manipulation:
        /// 1. For each direction, gets the neighbor's "self" flags
        /// 2. Maps them to the current cell's "neighbor" flags using directional indexing
        /// 3. Uses bit shifting to extract and reposition flag values
        ///
        /// Bit manipulation explanation:
        /// - Directions are indexed 0-5: Forward, Left, Up, Down, Right, Backward
        /// - Neighbor's "self" bit at position (5-i) corresponds to current cell's "neighbor" bit at position i
        /// - Formula: extract bit from neighbor, shift it to position 0, then shift to final neighbor position
        /// - Same logic applies to both airtight and mount point flags
        /// </summary>
        private void UpdateCell(Vector3I cell)
        {
            int state = Surfaces[cell];

            //MyLog.Default.Info($"[UpdateCell] {cell} {DebugSurfaceStateText(state)}");

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector3I ncell = cell + Directions[i];

                int ns;
                if (Surfaces.TryGetValue(ncell, out ns))
                {
                    // Extract neighbor's self-airtight bit and map to our neighbor-airtight bit
                    // (5-i) gets the opposite face, >> bitp normalizes to bit 0, << shifts to neighbor position
                    int bitp = 5 - i;
                    state |= (ns & 1 << bitp) >> bitp << i + (int)SurfaceFlags.NeighborAirtightOffset;

                    // Same for mount points: neighbor's self-mount becomes our neighbor-mount
                    bitp = 5 - i + (int)SurfaceFlags.SelfMountPointOffset;
                    state |= (ns & 1 << bitp) >> bitp << i + (int)SurfaceFlags.NeighborMountPointOffset;
                }
            }

            Surfaces[cell] = state;
        }


        public void GridMapperUpdate()
        {
            int loopCount = 0;

            while ((ExternalQueue.Count > 0 || GridQueue.Count > 0) && loopCount < Quota)
            {
                if (ExternalQueue.Count > 0)
                {
                    CrawlExternal();
                    loopCount++;
                }
                else if (GridQueue.Count > 0)
                {
                    CrawlGrid();
                    loopCount++;
                }

                if (ExternalQueue.Count == 0 && GridQueue.Count == 0)
                {
                    //MyLog.Default.Info($"[{Settings.Name}] Room mapping complete - External: {RoomBuffer[0].Count}, Rooms: {RoomBuffer.Count}");

                    lock (Rooms)
                    {
                        var temp = Rooms;
                        Rooms = RoomBuffer;
                        RoomBuffer = temp;
                    }

                    SurfaceCheckComplete?.Invoke();
                    return;
                }
            }
        }

        /// <summary>
        /// Resets any values to begin crawling
        /// </summary>
        private void BeginCrawl()
        {
            min = Grid.Min - 1;
            max = Grid.Max + 1;

            ExternalQueue.Clear();
            ExternalQueue.Enqueue(min);

            GridQueue.Clear();

            RoomBuffer.Clear();
            RoomBuffer.Add(new HashSet<Vector3I>() { min });
            RoomBuffer.Add(new HashSet<Vector3I>());

            Quota = (int)Math.Max(Math.Ceiling((max - min).Size / 60f), 1f);
        }

        private bool IsFullyAirtightQueued = false;

        /// <summary>
        /// Performs external flood fill to identify all cells connected to vacuum.
        ///
        /// This method implements a breadth-first search starting from external vacuum areas.
        /// It explores outward from known vacuum cells, marking connected empty spaces as external.
        /// The algorithm prioritizes fully airtight blocks to ensure proper room separation.
        ///
        /// Key concepts:
        /// - ExternalQueue: cells known to be exposed to vacuum
        /// - GridQueue: edge cells that may form new internal rooms
        /// - Surface bit checking: determines if adjacent faces are airtight
        /// </summary>
        private void CrawlExternal()
        {
            Vector3I cell = ExternalQueue.Dequeue();

            //MyLog.Default.Info($"[External] {cell}");

            for (int i = 0; i < 6; i++)
            {
                Vector3I ncell = cell + Directions[i];

                if (Vector3I.Min(ncell, min) != min || Vector3I.Max(ncell, max) != max)
                {
                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] {ncell} Out of Bounds");
                    continue;
                }

                if (RoomBuffer[0].Contains(ncell))
                {
                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] {ncell} Already External");
                    continue;
                }

                int surface;
                Surfaces.TryGetValue(ncell, out surface);

                if ((surface & 1 << (5 - i)) == 0)
                {
                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] {ncell} Empty - {DebugSurfaceStateText(surface)}");
                    ExternalQueue.Enqueue(ncell);
                    RoomBuffer[0].Add(ncell);
                }
                else if (GridQueue.Count == 0)
                {
                    if ((surface & (int)SurfaceFlags.SelfAirtightFull) == (int)SurfaceFlags.SelfAirtightFull)
                    {
                        IsFullyAirtightQueued = true;
                    }

                    GridQueue.Enqueue(ncell);
                    RoomBuffer[1].Add(ncell);

                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] {ncell} Edge Queued - {DebugSurfaceStateText(surface)}");
                }
                else if (!IsFullyAirtightQueued && 
                    (surface & (int)SurfaceFlags.SelfAirtightFull) == (int)SurfaceFlags.SelfAirtightFull)
                {
                    GridQueue.Dequeue();
                    GridQueue.Enqueue(ncell);
                    RoomBuffer[1].Clear();
                    RoomBuffer[1].Add(ncell);

                    IsFullyAirtightQueued = true;

                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] {ncell} Edge Requeued - {DebugSurfaceStateText(surface)}");
                }
                else
                {
                    
                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] {ncell} Edge - {DebugSurfaceStateText(surface)}");
                }
            }
        }

        /// <summary>
        /// Performs internal flood fill to identify sealed rooms within the grid.
        ///
        /// This method explores from edge cells that are not connected to external vacuum.
        /// It creates new rooms by grouping connected airtight cells, determining room boundaries
        /// based on surface airtightness between adjacent cells.
        ///
        /// Key logic:
        /// - Non-airtight cells expand existing rooms or create new ones
        /// - Fully airtight cells are treated as room boundaries/separators
        /// - Connection checking uses bit flags to determine if faces allow passage
        /// - The algorithm ensures each cell belongs to exactly one room
        /// </summary>
        private void CrawlGrid()
        {
            Vector3I cell = GridQueue.Dequeue();

            int roomKey = -1;
            int cellSurface;
            Surfaces.TryGetValue(cell, out cellSurface);
            bool isAirtightCell = (cellSurface & (int)SurfaceFlags.SelfAirtightFull) == (int)SurfaceFlags.SelfAirtightFull;
            
            if (!isAirtightCell)
            {
                if (!GetRoomKey(cell, out roomKey))
                {
                    roomKey = CreateRoom(cell);
                }
            }

            //MyLog.Default.Info($"[Grid Cell] {cell}, isAirtightCell: {isAirtightCell}, RoomKey: {roomKey}, Surface: {DebugSurfaceStateText(cellSurface)}");


            for (int i = 0; i < 6; i++)
            {
                Vector3I ncell = cell + Directions[i];

                if (RoomBuffer[0].Contains(ncell) )
                {
                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] already external");
                    continue;
                }

                int ncellSurface;
                Surfaces.TryGetValue(ncell, out ncellSurface);

                bool isAirtightFromCell = (cellSurface & 1 << i) != 0;
                bool isAirtightFromNeighbor = (ncellSurface & 1 << (5 - i)) != 0;

                int ncellRoomKey;
                GetRoomKey(ncell, out ncellRoomKey);

                //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] ncell - key {ncellRoomKey} - cellAirtight: {isAirtightFromCell}, ncellAirtight: {isAirtightFromNeighbor}");

                if (ncellRoomKey != -1)
                {
                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] ncell has room");
                    if (roomKey != ncellRoomKey && !(isAirtightFromCell || isAirtightFromNeighbor))
                    {
                        //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] cell and ncell should join");
                        if (roomKey != -1)
                        {
                            if (roomKey < ncellRoomKey)
                            {
                                RoomBuffer[roomKey].UnionWith(RoomBuffer[ncellRoomKey]);
                                RoomBuffer.RemoveAt(ncellRoomKey);
                            }
                            else
                            {
                                RoomBuffer[ncellRoomKey].UnionWith(RoomBuffer[roomKey]);
                                RoomBuffer.RemoveAt(roomKey);
                                roomKey = ncellRoomKey;
                            }
                        }
                    }
                }
                else if (roomKey != -1)
                {
                    if (!(isAirtightFromCell || isAirtightFromNeighbor))
                    {
                        RoomBuffer[roomKey].Add(ncell);
                        //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] Added ncell {ncell} to room {roomKey} - hasRoom: {HasRoom(ncell)}");
                    }


                }
                else
                {
                    if (!isAirtightFromNeighbor)
                    {
                        CreateRoom(ncell);
                    }


                }


                if (RoomBuffer[1].Contains(ncell))
                {
                    //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] Failed to Queue!");
                    continue;
                }

                //MyLog.Default.Info($"[{Tools.IndexToDirectionName(i)}] Queueing {ncell}");
                //queue cell and add it to the check cell list
                GridQueue.Enqueue(ncell);
                RoomBuffer[1].Add(ncell);
            }
        }

        private bool HasRoom(Vector3I cell)
        {
            for (int a = RoomBuffer.Count - 1; a > 1; a--)
            {
                if (RoomBuffer[a].Contains(cell))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasBeenSeen(Vector3I cell)
        {
            for (int a = RoomBuffer.Count - 1; a >= 0; a--)
            {
                if (RoomBuffer[a].Contains(cell))
                {
                    return true;
                }
            }
            return false;
        }

        private bool GetRoomKey(Vector3I cell, out int key)
        {
            for (int a = RoomBuffer.Count - 1; a > 1; a--)
            {
                if (RoomBuffer[a].Contains(cell))
                {
                    key = a;
                    return true;
                }
            }

            key = -1;
            return false;
        }



        private int CreateRoom(Vector3I cell)
        {
            int roomkey = RoomBuffer.Count;
            RoomBuffer.Add(new HashSet<Vector3I>() { cell });
            MyLog.Default.Info($"Created new room {roomkey} starting at {cell}");
            return roomkey;
        }

        public string DebugSurfaceStateText(int state)
        {
            StringBuilder sb = new StringBuilder("Surfaces: ");
            for (int i = 0; i < 30; i++)
            {
                if (i % 6 == 0)
                {
                    sb.Append(" - ");
                }
                sb.Append($"{((state & 1 << i) != 0 ? 1 : 0)} ");
            }

            return sb.ToString();
        }


        public int[] GetExposedSurfacesByDirection(Vector3I min, Vector3I max)
        {
            int[] exposedSurfaces = new int[6];
            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        Vector3I cell = new Vector3I(x, y, z);
                        int cellState = Surfaces[cell];

                        for (int i = 0; i < Directions.Length; i++)
                        {
                            Vector3I n = cell + Directions[i];

                            if (!(n.X >= min.X && n.Y >= min.Y && n.Z >= min.Z &&
                                n.X < max.X && n.Y < max.Y && n.Z < max.Z))
                            {
                                if ((cellState & 1 << i + (int)SurfaceFlags.NeighborAirtightOffset) == 0 && Rooms[0].Contains(n))
                                {
                                    if (!((cellState & 1 << i + (int)SurfaceFlags.NeighborMountPointOffset) != 0 && (cellState & 1 << i + (int)SurfaceFlags.SelfMountPointOffset) != 0))
                                    {
                                        exposedSurfaces[i]++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return exposedSurfaces;
        }
    }
}
