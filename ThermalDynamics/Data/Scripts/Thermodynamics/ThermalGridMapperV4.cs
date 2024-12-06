using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {
        public enum SurfaceFlags
        {
            NeighborAirtightOffset = 6,
            SelfMountPointOffset = 12,
            NeighborMountPointOffset = 18,

            SelfAirtightForward = 1,
            SelfAirtightLeft = 2,
            SelfAirtightUp = 4,
            SelfAirtightDown = 8,
            SelfAirtightRight = 16,
            SelfAirtightBackword = 32,
            SelfAirtightFull = 63,

            NeighborAirtightForward = 64,
            NeighborAirtightLeft = 128,
            NeighborAirtightUp = 256,
            NeighborAirtightDown = 512,
            NeighborAirtightRight = 1024,
            NeighborAirtightBackword = 2048,
            NeighborAirtightFull = 992,

            SelfMountPointForward = 4096,
            SelfMountPointLeft = 8192,
            SelfMountPointUp = 16384,
            SelfMountPointDown = 32768,
            SelfMountPointRight = 65536,
            SelfMountPointBackword = 131072,
            SelfMountPointFull = 64512,

            NeighborMountPointForward = 262144,
            NeighborMountPointLeft = 524288,
            NeighborMountPointUp = 1048576,
            NeighborMountPointDown = 2097152,
            NeighborMountPointRight = 4194304,
            NeighborMountPointBackword = 8388608,
            NeighborMountPointFull = 8126464
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
        /// The maximum number of frames each process can take
        /// </summary>
        public int FramesToCompleteWork = 60;
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
        /// </summary>
        public List<HashSet<Vector3I>> Rooms = new List<HashSet<Vector3I>>() { new HashSet<Vector3I>() };
        private List<HashSet<Vector3I>> RoomBuffer = new List<HashSet<Vector3I>>() { new HashSet<Vector3I>() };

        private int Quota;

        private Queue<Vector3I> ExternalQueue = new Queue<Vector3I>();
        private Queue<Vector3I> SolidQueue = new Queue<Vector3I>();
        private HashSet<Vector3I> Solid = new HashSet<Vector3I>();
        private Queue<Vector3I> RoomQueue = new Queue<Vector3I>();


        private Vector3I min;
        private Vector3I max;

        public void OnBlockAddOrUpdate(IMySlimBlock block)
        {
            //MyLog.Default.Info($"OnBlockAddOrUpdate: {block.BlockDefinition.Id}");

            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;
            Vector3I position = block.Position;

            if (block.FatBlock is IMyDoor)
            {
                ((IMyDoor)block.FatBlock).DoorStateChanged += (state) => OnBlockAddOrUpdate(block);
            }


            Queue<Vector3I> processQueue = new Queue<Vector3I>();

            //List<MyCubeBlockDefinition.MountPoint> mounts = new List<MyCubeBlockDefinition.MountPoint>();
            MyCubeBlockDefinition def = block.BlockDefinition as MyCubeBlockDefinition;
            bool isAirtight = def?.IsAirTight == true;
            Matrix matrix;
            MyBlockOrientation orientation = block.Orientation;
            orientation.GetMatrix(out matrix);
            matrix.TransposeRotationInPlace();
            //MyCubeGrid.TransformMountPoints(mounts, def, def.MountPoints, ref orientation);

            // look at all cells within the block
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


                        // assign full airtightness value if airtight
                        if (isAirtight)
                        {
                            state = (int)SurfaceFlags.SelfAirtightFull;
                        }

                        for (int a = 0; a < Directions.Length; a++)
                        {
                            Vector3I dir = Directions[a];
                            Vector3I ncell = cell + dir;

                            // if the current cell is not airtight
                            // perform local airtightness check
                            if (!isAirtight)
                            {
                                MyCubeBlockDefinition.MyCubePressurizationMark mark = def.IsCubePressurized[tcell][dir];
                                if (mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways)
                                {
                                    state |= 1 << a;
                                }
                                else if (block.FatBlock is IMyDoor)
                                {
                                    IMyDoor door = (IMyDoor)block.FatBlock;
                                    if (mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed)
                                    {
                                        //MyLog.Default.Info($"PressurizedClosed - DoorStatus: {door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing} ");
                                        if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing)
                                        {
                                            state |= 1 << a;
                                        }
                                    }
                                    else if (door.IsFullyClosed)
                                    {
                                        //M.Default.Info($"Door is fully closed");
                                        if (door is MyAirtightSlideDoor && dir == Vector3I.Forward ||
                                            door is MyAirtightDoorGeneric && (dir == Vector3I.Forward || dir == Vector3I.Backward))
                                        {
                                            state |= 1 << a;
                                        }
                                        else
                                        {
                                            bool isValid = true;
                                            MyCubeBlockDefinition.MountPoint[] mountPoints = def.MountPoints;
                                            for (int i = 0; i < mountPoints.Length; i++)
                                            {
                                                if (dir == mountPoints[i].Normal)
                                                {
                                                    isValid = false;
                                                }
                                            }

                                            if (isValid)
                                            {
                                                state |= 1 << a;
                                            }
                                        }
                                    }
                                }
                            }

                            //perform mount point check
                            //for (int b = 0; b < mounts.Count; b++)
                            //{
                            //    MyCubeBlockDefinition.MountPoint m = mounts[b];
                            //    if (!m.Enabled || m.Normal != dir)
                            //    {
                            //        continue;
                            //    }

                            //    Vector3 mmin = Vector3.Min(m.Start, m.End) - local;
                            //    Vector3 mmax = Vector3.Max(m.Start, m.End) - local;
                            //    BoundingBox mountBox = new BoundingBox(mmin, mmax);
                            //    BoundingBox cellbox = mountBounds[a];

                            //    if (cellbox.Intersects(mountBox))
                            //    {
                            //        state |= 1 << a + (int)SurfaceFlags.SelfMountPointOffset;
                            //    }
                            //}

                            int ns;
                            if (Surfaces.TryGetValue(ncell, out ns))
                            {
                                // get the neighbors "self" faces and set them as our neighbor faces

                                // bit shift to get the opposite face of the neighbor
                                // then move the 1 or 0 back to the first position
                                // then shift it to its neighbor position
                                int bitp = 5 - a;
                                state |= (ns & 1 << bitp) >> bitp << a + (int)SurfaceFlags.NeighborAirtightOffset;

                                //bitp = 5 - a + (int)SurfaceFlags.SelfMountPointOffset;
                                //state |= (ns & 1 << bitp) >> bitp << a + (int)SurfaceFlags.SelfMountPointOffset;

                                processQueue.Enqueue(ncell);
                            }
                        }

                        //MyLog.Default.Info(DebugSurfaceStateText(state));

                        // adds or updates the state of the block
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

            BeginRoomCrawl();
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
                                //surface &= ~(1 << 5 - i + (int)SurfaceFlags.NeighborMountPointOffset);
                                Surfaces[n] = surface;

                                //MyLog.Default.Info(DebugSurfaceStateText(surface));
                            }
                        }
                    }
                }
            }



            BeginRoomCrawl();
        }

        private void UpdateCell(Vector3I cell)
        {
            int state = Surfaces[cell];
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector3I ncell = cell + Directions[i];

                // get the neighbors "self" faces and set them as our neighbor faces

                // bit shift to get the opposite face of the neighbor
                // then move the 1 or 0 back to the first position
                // then shift it to its neighbor position
                int ns;
                Surfaces.TryGetValue(ncell, out ns);

                int bitp = 5 - i;
                state |= (ns & 1 << bitp) >> bitp << i + (int)SurfaceFlags.NeighborAirtightOffset;

                bitp = 5 - i + (int)SurfaceFlags.SelfMountPointOffset;
                state |= (ns & 1 << bitp) >> bitp << i + (int)SurfaceFlags.SelfMountPointOffset;

            }
            Surfaces[cell] = state;
        }


        public void GridMapperUpdate()
        {
            int loopCount = 0;
            while (ExternalQueue.Count > 0 && loopCount < Quota) // + SolidQueue.Count + RoomQueue.Count
            {
                if (ExternalQueue.Count > 0)
                {
                    CrawlExternal();
                    loopCount++;
                }
                //else if (SolidQueue.Count > 0)
                //{
                //    CrawlSolids();
                //    loopCount++;
                //}
                //else if (RoomQueue.Count > 0)
                //{
                //    CrawlRooms();
                //    loopCount++;
                //}

                if (ExternalQueue.Count == 0 && loopCount != 0)
                {
                    lock (Rooms)
                    {
                        var temp = Rooms;
                        Rooms = RoomBuffer;
                        RoomBuffer = temp;
                    }

                    SurfaceCheckComplete?.Invoke();
                }
            }
        }

        /// <summary>
        /// Resets any values to begin crawling
        /// </summary>
        private void BeginRoomCrawl()
        {
            min = Grid.Min - 1;
            max = Grid.Max + 1;

            ExternalQueue.Clear();
            ExternalQueue.Enqueue(min);

            RoomQueue.Clear();
            RoomBuffer.Clear();
            RoomBuffer.Add(new HashSet<Vector3I>() { min });

            SolidQueue.Clear();
            Solid.Clear();

            Quota = (int)Math.Max(Math.Ceiling((max - min).Size / 60f), 1f);
        }

        private void CrawlExternal()
        {
            Vector3I cell = ExternalQueue.Dequeue();

            for (int i = 0; i < 6; i++)
            {
                // get the neighbor location
                Vector3I ncell = cell + Directions[i];

                // skip neighbor if its out of bounds or has already been checked by external
                if (Vector3I.Min(ncell, min) != min || Vector3I.Max(ncell, max) != max || RoomBuffer[0].Contains(ncell))
                    continue;

                int surface;
                Surfaces.TryGetValue(ncell, out surface);
                // if one or both faces in the direction are airtight queue this as a solid block
                if ((surface & 1 << 5 - i) != 0 || (surface & 1 << i) != 0)
                {
                    if (SolidQueue.Count == 0)
                    {
                        SolidQueue.Enqueue(ncell);
                    }
                    //MyLog.Default.Info($"{cell} + {Directions[i]}: is solid");
                    continue;
                }

                //MyLog.Default.Info($"{cell} + {Directions[i]}: queued");

                // enqueue empty or non airtight nodes
                ExternalQueue.Enqueue(ncell);
                RoomBuffer[0].Add(ncell);
            }
        }

        private void CrawlSolids()
        {
            Vector3I cell = SolidQueue.Dequeue();

            int surface;
            Surfaces.TryGetValue(cell, out surface);

            //MyLog.Default.Info($"Crawl Solid {cell} - {DebugSurfaceStateText(surface)}");

            for (int i = 0; i < 6; i++)
            {
                // get the neighbor location
                Vector3I ncell = cell + Directions[i];

                if (Solid.Contains(ncell))
                    continue;

                // if surface is not airtight check to see if its exterior
                // if it is not exterior queue it for review
                if ((surface & 1 << i + (int)SurfaceFlags.NeighborAirtightOffset) == 0)
                {


                    if (!RoomBuffer[0].Contains(ncell))
                    {
                        //MyLog.Default.Info($"{cell} + {Directions[i]}: found room");
                        RoomQueue.Enqueue(ncell);
                    }

                    //MyLog.Default.Info($"{cell} + {Directions[i]}: exterior");
                    continue;
                }

                //MyLog.Default.Info($"{cell} + {Directions[i]}: queue solid");
                // enqueue empty or non airtight nodes
                SolidQueue.Enqueue(ncell);
                Solid.Add(ncell);
            }
        }

        private void CrawlRooms()
        {
            Vector3I cell = RoomQueue.Dequeue();

            int cellRoomIndex;
            if (!GetRoomKey(cell, out cellRoomIndex))
            {
                cellRoomIndex = RoomBuffer.Count;
                RoomBuffer.Add(new HashSet<Vector3I>() { cell });
            }

            //MyLog.Default.Info($"room index: {cellRoomIndex}");

            for (int i = 0; i < 6; i++)
            {
                // get the neighbor location
                Vector3I ncell = cell + Directions[i];

                //MyLog.Default.Info($"{cell} + {Directions[i]}: is exposed {RoomBuffer[0].Contains(ncell)}");

                int surface;
                Surfaces.TryGetValue(ncell, out surface);
                // if surface direction is airtight or the cell already has a room do not queue

                // check neighbor rearface
                if ((surface & 1 << 5 - i) != 0)
                {
                    //MyLog.Default.Info($"{ncell}: neighbor face");
                    continue;
                }

                // check cell forward face
                if ((surface & 1 << i + (int)SurfaceFlags.NeighborAirtightOffset) != 0)
                {
                    //MyLog.Default.Info($"{ncell}: cell face");
                    continue;
                }

                // dont add this cell if the face is airtight or the block already has a room
                if (HasRoom(ncell))
                {
                    //MyLog.Default.Info($"{ncell}: already roomed");
                    continue;
                }

                //MyLog.Default.Info($"{ncell}: queue");
                RoomQueue.Enqueue(ncell);
                RoomBuffer[cellRoomIndex].Add(ncell);
            }
        }

        private bool HasRoom(Vector3I cell)
        {
            for (int a = RoomBuffer.Count - 1; a > 0; a--)
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
            for (int a = RoomBuffer.Count - 1; a > 0; a--)
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

                            // if i am looking at an outword facing surface of this block
                            if (!(n.X >= min.X && n.Y >= min.Y && n.Z >= min.Z &&
                                n.X < max.X && n.Y < max.Y && n.Z < max.Z))
                            {

                                // if the current cell is not airtight
                                // or the neighbor connected in this direciton is not airtight
                                // and the neighbor cell is exposed
                                if ((cellState & 1 << i + (int)SurfaceFlags.NeighborAirtightOffset) == 0 && Rooms[0].Contains(n))
                                {
                                    exposedSurfaces[i]++;
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