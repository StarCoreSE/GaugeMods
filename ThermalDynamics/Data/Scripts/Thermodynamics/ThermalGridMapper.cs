using ProtoBuf.Meta;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Definitions;
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

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {
        private Vector3I[] neighbors = new Vector3I[6]
        {
            new Vector3I(1, 0, 0), // left
            new Vector3I(-1, 0, 0), // right
            new Vector3I(0, 1, 0), // up
            new Vector3I(0, -1, 0), // down
            new Vector3I(0, 0, 1), // forward
            new Vector3I(0, 0, -1) // backward
        };

        public HashSet<Vector3I> ExteriorNodes = new HashSet<Vector3I>();
        public Queue<Vector3I> ExteriorQueue = new Queue<Vector3I>();

        private Vector3I min;
        private Vector3I max;

        public int NodeCountPerFrame = 1;
        public bool NodeUpdateComplete = false;

        public Dictionary<Vector3I, int> BlockNodes = new Dictionary<Vector3I, int>();

        private void MapBlocks(IMySlimBlock block, bool isNew = false, IMySlimBlock removed = null)
        {
            int state = 0;
            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;

            if (isNew)
            {
                if (Vector3I.Min(Grid.Min - 1, this.min) != this.min ||
                    Vector3I.Max(Grid.Max + 1, this.max) != this.max)
                    ResetMapper();
            }

            HashSet<IMySlimBlock> processedNeighbours = new HashSet<IMySlimBlock>();
            processedNeighbours.Add(removed);

            MyCubeBlockDefinition def = block.BlockDefinition as MyCubeBlockDefinition;
            Matrix blockMatrix;
            block.Orientation.GetMatrix(out blockMatrix);
            blockMatrix.TransposeRotationInPlace();

            bool isAirTight = def?.IsAirTight == true;
            if (isAirTight)
            {
                state = 63;
            }

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {

                        Vector3I node = new Vector3I(x, y, z);

                        for (int i = 0; i < neighbors.Length; i++)
                        {
                            Vector3I n = node + neighbors[i];
                            Vector3 direction1 = node - n;
                            Vector3 direction2 = n - node;

                            // neighbour is the same block
                            if (n.X >= min.X && n.Y >= min.Y && n.Z >= min.Z &&
                                n.X < max.X && n.Y < max.Y && n.Z < max.Z)
                            {

                                if (IsAirtight(ref block, ref def, ref node, ref direction2, ref blockMatrix))
                                {
                                    state |= 1 << i;
                                }

                                if (IsAirtight(ref block, ref def, ref n, ref direction1, ref blockMatrix))
                                {
                                    state |= 1 << i + 6;
                                }
                            }
                            else
                            {
                                IMySlimBlock nblock = Grid.GetCubeBlock(n);
                                if (nblock == null)
                                {
                                    continue;
                                }

                                // update all neighbor blocks if this block was just added to the grid
                                if (isNew && !processedNeighbours.Contains(nblock))
                                {
                                    MapBlocks(nblock);
                                    processedNeighbours.Add(nblock);
                                }

                                MyCubeBlockDefinition ndef = nblock.BlockDefinition as MyCubeBlockDefinition;
                                Matrix nMatrix;
                                nblock.Orientation.GetMatrix(out nMatrix);
                                nMatrix.TransposeRotationInPlace();

                                if (IsAirtight(ref block, ref def, ref node, ref direction2, ref blockMatrix))
                                {
                                    state |= 1 << i;
                                }

                                if (ndef?.IsAirTight == true ||
                                    IsAirtight(ref nblock, ref ndef, ref n, ref direction1, ref nMatrix))
                                {
                                    state |= 1 << i + 6;
                                }
                            }
                        }

                        if (isNew)
                        {
                            BlockNodes.Add(node, state);
                        }
                        else
                        {
                            BlockNodes[node] = state;
                        }

                    }
                }
            }
        }

        private void UpdateNodeState(ref int state, Vector3I node, IMySlimBlock block, MyCubeBlockDefinition def, Matrix blockMatrix)
        {
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector3I neighborPosition = node + neighbors[i];
                IMySlimBlock neighborBlock = Grid.GetCubeBlock(neighborPosition);

                // Convert Vector3I to Vector3 for the direction
                Vector3 direction = new Vector3(neighbors[i]);

                if (neighborPosition.X >= block.Min.X && neighborPosition.Y >= block.Min.Y && neighborPosition.Z >= block.Min.Z &&
                    neighborPosition.X < block.Max.X && neighborPosition.Y < block.Max.Y && neighborPosition.Z < block.Max.Z)
                {
                    // Need to pass a reference to a Vector3, not Vector3I
                    if (IsAirtight(ref block, ref def, ref neighborPosition, ref direction, ref blockMatrix))
                    {
                        state |= 1 << i;
                    }
                }
                else
                {
                    // Process external or neighboring blocks
                    if (neighborBlock != null)
                    {
                        MyCubeBlockDefinition neighborDef = neighborBlock.BlockDefinition as MyCubeBlockDefinition;
                        // Also convert Vector3I to Vector3 here before passing
                        if (IsAirtight(ref neighborBlock, ref neighborDef, ref neighborPosition, ref direction, ref blockMatrix))
                        {
                            state |= 1 << i;
                        }
                    }
                }
            }
        }

        private void ProcessNeighborBlock(Vector3I neighborPosition, bool isNew, ref int state, ref HashSet<IMySlimBlock> processedNeighbors, int directionIndex, ref Matrix blockMatrix)
        {
            IMySlimBlock nblock = Grid.GetCubeBlock(neighborPosition);
            if (nblock == null) return;

            if (isNew && !processedNeighbors.Contains(nblock))
            {
                MapBlocks(nblock);
                processedNeighbors.Add(nblock);
            }

            MyCubeBlockDefinition ndef = nblock.BlockDefinition as MyCubeBlockDefinition;
            var nMatrix = new Matrix();
            nblock.Orientation.GetMatrix(out nMatrix);
            nMatrix.TransposeRotationInPlace();

            // Convert Vector3I to Vector3 for method calls
            Vector3 directionVec = new Vector3(neighbors[directionIndex]);
            Vector3 oppositeDirectionVec = new Vector3(neighbors[(directionIndex + 6) % 12]);

            if (IsAirtight(ref nblock, ref ndef, ref neighborPosition, ref directionVec, ref nMatrix))
                state |= 1 << directionIndex;

            if (ndef?.IsAirTight == true || IsAirtight(ref nblock, ref ndef, ref neighborPosition, ref oppositeDirectionVec, ref nMatrix))
                state |= 1 << (directionIndex + 6);
        }

        private void MapBlockRemove(IMySlimBlock block) 
        {
            Vector3I min = block.Min;
            Vector3I max = block.Max + 1;

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        Vector3I node = new Vector3I(x, y, z);

                        int flag = BlockNodes[node];

                        for (int i = 6; i < 12; i++) 
                        {
                            if ((flag & i) != 0) 
                            {
                                Vector3I n =  node + neighbors[i - 6];

                                if (!(n.X >= min.X && n.Y >= min.Y && n.Z >= min.Z &&
                                n.X < max.X && n.Y < max.Y && n.Z < max.Z)) 
                                {
                                    MapBlocks(Grid.GetCubeBlock(n), false, block);
                                }

                            }
                        }

                        BlockNodes.Remove(node);
                    }
                }
            }
        }

        private bool IsAirtight(ref IMySlimBlock block, ref MyCubeBlockDefinition def, ref Vector3I pos, ref Vector3 normal, ref Matrix matrix)
        {
            Vector3 position = Vector3.Zero;
            if (block.FatBlock != null)
            {
                position = pos - block.FatBlock.Position;
            }

            IMyDoor door = block.FatBlock as IMyDoor;
            bool isDoorClosed = door != null && (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing);

            Vector3I transformedNormal = Vector3I.Round(Vector3.Transform(normal, matrix));
            Vector3 value = Vector3.Transform(position, matrix) + def.Center;
            Vector3I roundedValue = Vector3I.Round(value);

            if (def.IsCubePressurized.ContainsKey(roundedValue) && def.IsCubePressurized[roundedValue].ContainsKey(transformedNormal))
            {
                switch (def.IsCubePressurized[roundedValue][transformedNormal])
                {
                    case MyCubeBlockDefinition.MyCubePressurizationMark.NotPressurized:
                        return false;
                    case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways:
                        return true;
                    case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed:
                        return isDoorClosed;
                }
            }

            // Default to not airtight if the key is not found or if no specific condition matches
            return isDoorClosed && door != null && IsDoorAirtight(ref door, ref transformedNormal, ref def);
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
                MyCubeBlockDefinition.MountPoint mountPoint = mountPoints[i];
                if (normal == mountPoint.Normal)
                {
                    return false;
                }
            }

            return true;
        }

        public void ResetMapper()
        {
            min = Grid.Min - 1;
            max = Grid.Max + 1;

            ExteriorQueue.Clear();
            ExteriorQueue.Enqueue(min);

            ExteriorNodes.Clear();
            ExteriorNodes.Add(min);

            NodeCountPerFrame = Math.Max((int)((max - min).Size / 60f), 1);
            NodeUpdateComplete = false;
        }

        public void MapExterior()
        {
            if (ExteriorQueue.Count == 0) return;

            int loopCount = 0;

            CrawlOutsideV2(ref loopCount);
        }

        private void CrawlOutsideV2(ref int loopCount)
        {
            while (ExteriorQueue.Count > 0 && loopCount < NodeCountPerFrame)
            {
                loopCount++;
                Vector3I node = ExteriorQueue.Dequeue();

                for (int i = 0; i < neighbors.Length; i++)
                {
                    // get the neighbor location
                    Vector3I n = node + neighbors[i];

                    // skip if the neighbor is out of bounds or already checked.
                    if (Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max || ExteriorNodes.Contains(n))
                        continue;

                    int flag;
                    if (BlockNodes.TryGetValue(n, out flag))
                    {
                        // get the alternate direction left/right, up/down
                        int direction;
                        if (i % 2 == 0)
                        {
                            direction = 1 << (i + 1);
                        }
                        else
                        {
                            direction = 1 << (i - 1);
                        }

                        // do not queue this block if it is airtight
                        if ((flag & direction) == direction)
                        {
                            continue;
                        }
                    }

                    // enqueue empty or non airtight nodes
                    ExteriorNodes.Add(n);
                    ExteriorQueue.Enqueue(n);
                }
            }
        }
    }
}