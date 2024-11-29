using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "Gauge_LG_CoolantPump")]
    public class CoolantPump : MyGameLogicComponent
    {

        private static string[] pipes = new string[]
        {
            "Gauge_LG_CoolantPipe_Straight",
            "Gauge_LG_CoolantPipe_Straight_DoubleSink",
            "Gauge_LG_CoolantPipe_Straight_Sink",
            "Gauge_LG_CoolantPipe_Corner",
            "Gauge_LG_CoolantPipe_Corner_DoubleSink",
            "Gauge_LG_CoolantPipe_Corner_SingleSink",
            "Gauge_LG_CoolantPump"
        };

        private IMyUpgradeModule Block;
        private ThermalGrid thermalGrid;


        public ThermalCell[] Loop = null;
        public float LoopTemp = 0;

        private float conductivity = 1500;
        public float area;
        private float C;
        private float thermalMassInv;


        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Block = (IMyUpgradeModule)Entity;

            // TODO calculate what the actual pipe size is

            // the pipe is a cylindar
            float r = Block.CubeGrid.GridSize*10f;
            area = (float)(2f * Math.PI * r * Block.CubeGrid.GridSize);

            //float volume = (float)(Math.PI * r * r * Block.CubeGrid.GridSize);

            // density * volume
            float mass = 200f;

            // c =  Temp / (watt * meter)
            float specificHeat = 1000f;
            C = 1 / (specificHeat * mass * Block.CubeGrid.GridSize);
            thermalMassInv = 1f / (specificHeat * mass);

            thermalGrid = Block.CubeGrid.GameLogic.GetAs<ThermalGrid>();

            Block.CubeGrid.OnBlockAdded += blockChanged;
            Block.CubeGrid.OnBlockRemoved += blockChanged;
        }

        private void blockChanged(IMySlimBlock block)
        {
            if (pipes.Contains(block.BlockDefinition.Id.SubtypeId.String)) 
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        public void Simulate() 
        {
            for (int i = 0; i < Loop.Length; i++)
            {

                ThermalCell c = Loop[i];

                float myDT = conductivity * area * (c.Temperature - LoopTemp);
                float cDT = c.Definition.Conductivity * area * (LoopTemp - c.Temperature);


                float myDelta = (C * myDT * thermalMassInv) * Settings.Instance.TimeScaleRatio;
                float cDelta = (c.C * cDT * c.ThermalMassInv) * Settings.Instance.TimeScaleRatio;

                LoopTemp = Math.Max(0, LoopTemp + myDelta);
                c.Temperature = Math.Max(0, c.Temperature + cDelta);

                //MyLog.Default.Info($"index: {i}, temp difference: {c.Temperature - LoopTemp} --- delta in: {myDelta}, to block: {cDelta}");

            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Loop == null)
            {
                createLink();
            }

            if (Loop == null) 
            {
                if (thermalGrid.Pumps.Contains(this))
                {
                    thermalGrid.Pumps.Remove(this);
                }
                return;
            }

            // TODO: change this when blocks are converted to fatblocks
            foreach (ThermalCell c in Loop) 
            {
                if (c.Block == null) 
                {
                    Loop = null;
                    LoopTemp = 0;
                    
                    if (thermalGrid.Pumps.Contains(this))
                    {
                        thermalGrid.Pumps.Remove(this);
                    }

                    break;
                }
            }

            if (!thermalGrid.Pumps.Contains(this))
            {
                thermalGrid.Pumps.Add(this);
            }

        }

        private void createLink() 
        {
            Matrix m;
            Block.Orientation.GetMatrix(out m);
            Vector3I[] directions = Base6Directions.IntDirections;

            Vector3I right;
            Vector3I.Transform(ref directions[(int)Base6Directions.Direction.Right], ref m, out right);
            IMySlimBlock next = Block.CubeGrid.GetCubeBlock(Block.Position + right);

            List<ThermalCell> loop = new List<ThermalCell>() 
            {
                thermalGrid.Get(Block.Position)
            };

            if (AddLink(Block.Position, next, ref loop)) 
            {
                Loop = loop.ToArray();
            }
        }

        private bool AddLink(Vector3I lastLocation, IMySlimBlock b, ref List<ThermalCell> loop) 
        {
            if (b == null) return false;

            string subtype = b.BlockDefinition.Id.SubtypeId.String;
            string subtypelower = subtype.ToLower();
            if (!pipes.Contains(subtype)) return false;

            Matrix m;
            b.Orientation.GetMatrix(out m);
            Vector3I[] directions = Base6Directions.IntDirections;

            Vector3I left;
            Vector3I.Transform(ref directions[(int)Base6Directions.Direction.Left], ref m, out left);

            Vector3I up;
            Vector3I.Transform(ref directions[(int)Base6Directions.Direction.Up], ref m, out up);

            Vector3I forward;
            Vector3I.Transform(ref directions[(int)Base6Directions.Direction.Forward], ref m, out forward);


            if (subtypelower.Contains("coolantpump"))
            {
                if (b == Block.SlimBlock)
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} Loop complete");
                    return true;
                }
                else if (lastLocation == b.Position + left)
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: left");
                    loop.Add(thermalGrid.Get(b.Position));
                    IMySlimBlock next = b.CubeGrid.GetCubeBlock(b.Position - left);
                    return AddLink(b.Position, next, ref loop);
                }
                else if (lastLocation == b.Position - left) 
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: right");
                    loop.Add(thermalGrid.Get(b.Position));
                    IMySlimBlock next = b.CubeGrid.GetCubeBlock(b.Position + left);
                    return AddLink(b.Position, next, ref loop);
                }
                else
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: incorrectly");
                    return false;
                }
            }
            else if (subtypelower.Contains("corner"))
            {
                if (lastLocation == b.Position + left)
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: left");
                    loop.Add(thermalGrid.Get(b.Position));
                    IMySlimBlock next = b.CubeGrid.GetCubeBlock(b.Position + forward);
                    return AddLink(b.Position, next, ref loop);
                }
                else if (lastLocation == b.Position + forward)
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: forward");
                    loop.Add(thermalGrid.Get(b.Position));
                    IMySlimBlock next = b.CubeGrid.GetCubeBlock(b.Position + left);
                    return AddLink(b.Position, next, ref loop);
                }
                else
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: incorrectly");

                    return false;
                }
            }
            else 
            {
                if (lastLocation == b.Position + forward)
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: forward");
                    loop.Add(thermalGrid.Get(b.Position));
                    IMySlimBlock next = b.CubeGrid.GetCubeBlock(b.Position - forward);
                    return AddLink(b.Position, next, ref loop);
                }
                else if (lastLocation == b.Position - forward)
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: backword");
                    loop.Add(thermalGrid.Get(b.Position));
                    IMySlimBlock next = b.CubeGrid.GetCubeBlock(b.Position + forward);
                    return AddLink(b.Position, next, ref loop);
                }
                else
                {
                    MyLog.Default.Info($"[{Settings.Name}] {subtype} connected: incorrectly");

                    return false;
                }
            }
        }
    }
}
