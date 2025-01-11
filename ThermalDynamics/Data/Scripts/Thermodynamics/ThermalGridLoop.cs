using System;
using System.Collections.Generic;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {
        public static readonly Dictionary<string, Vector3I[]> CoolantPlateDirections = new Dictionary<string, Vector3I[]>
        {
            { "Gauge_LG_CoolantPipe_Straight", new Vector3I[0] },
            { "Gauge_LG_CoolantPipe_Straight_DoubleSink", new Vector3I[] { Vector3I.Left, Vector3I.Right } },
            { "Gauge_LG_CoolantPipe_Straight_SingleSink", new Vector3I[] { Vector3I.Right } },
            { "Gauge_LG_CoolantPipe_Corner", new Vector3I[0]  },
            { "Gauge_LG_CoolantPipe_Corner_DoubleSink", new Vector3I[] { Vector3I.Backward, Vector3I.Right } },
            { "Gauge_LG_CoolantPipe_Corner_SingleSink", new Vector3I[] { Vector3I.Up } },
            { "Gauge_LG_CoolantPump", new Vector3I[0]  },
            
            { "Gauge_SG_CoolantPipe_Straight", new Vector3I[0] },
            { "Gauge_SG_CoolantPipe_Straight_DoubleSink", new Vector3I[] { Vector3I.Left, Vector3I.Right } },
            { "Gauge_SG_CoolantPipe_Straight_SingleSink", new Vector3I[] { Vector3I.Right } },
            { "Gauge_SG_CoolantPipe_Corner", new Vector3I[0]  },
            { "Gauge_SG_CoolantPipe_Corner_DoubleSink", new Vector3I[] { Vector3I.Backward, Vector3I.Right } },
            { "Gauge_SG_CoolantPipe_Corner_SingleSink", new Vector3I[] { Vector3I.Up } },
            { "Gauge_SG_CoolantPump", new Vector3I[0]  },
        };

        public static readonly Dictionary<string, Vector3I[]> CoolantPipeLinkDirections = new Dictionary<string, Vector3I[]>
        {
            { "Gauge_LG_CoolantPipe_Straight", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },
            { "Gauge_LG_CoolantPipe_Straight_DoubleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },
            { "Gauge_LG_CoolantPipe_Straight_SingleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },
            { "Gauge_LG_CoolantPipe_Corner", new Vector3I[] { Vector3I.Forward, Vector3I.Left } },
            { "Gauge_LG_CoolantPipe_Corner_DoubleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Left } },
            { "Gauge_LG_CoolantPipe_Corner_SingleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Left } },
            { "Gauge_LG_CoolantPump", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },

            { "Gauge_SG_CoolantPipe_Straight", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },
            { "Gauge_SG_CoolantPipe_Straight_DoubleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },
            { "Gauge_SG_CoolantPipe_Straight_SingleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },
            { "Gauge_SG_CoolantPipe_Corner", new Vector3I[] { Vector3I.Forward, Vector3I.Left } },
            { "Gauge_SG_CoolantPipe_Corner_DoubleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Left } },
            { "Gauge_SG_CoolantPipe_Corner_SingleSink", new Vector3I[] { Vector3I.Forward, Vector3I.Left } },
            { "Gauge_SG_CoolantPump", new Vector3I[] { Vector3I.Forward, Vector3I.Backward } },
        };


        public static readonly HashSet<string> CoolantPumpNames = new HashSet<string>
        {
            "Gauge_LG_CoolantPump",
            "Gauge_SG_CoolantPump"
        };

        internal List<ThermalLoop> ThermalLoops = new List<ThermalLoop>();


        internal void OnAddDoCoolantCheck(ThermalCell cell)
        {
            if (!CoolantPipeLinkDirections.ContainsKey(cell.Block.BlockDefinition.Id.SubtypeId.ToString())) return;

            StartCoolantCrawl(cell);

        }

        internal void OnRemoveDoCoolantCheck(ThermalCell cell)
        {
            if (!CoolantPipeLinkDirections.ContainsKey(cell.Block.BlockDefinition.Id.SubtypeId.ToString())) return;

            for (int i = 0; i < ThermalLoops.Count; i++) {
                if (ThermalLoops[i].Loop.Contains(cell)) 
                {
                    ThermalLoops.RemoveAt(i);
                    break;
                }
            }

        }

        private void StartCoolantCrawl(ThermalCell cell)
        {
            Matrix m;
            cell.Block.Orientation.GetMatrix(out m);

            List<ThermalCell> loop = new List<ThermalCell> { cell };

            Vector3I[] directions = CoolantPipeLinkDirections[cell.Block.BlockDefinition.Id.SubtypeId.ToString()];

            Vector3I position = cell.Block.Min;

            Vector3I start;
            Vector3I.Transform(ref directions[0], ref m, out start);
            if (cell.Block.BlockDefinition.Id.SubtypeId.ToString() == "Gauge_SG_CoolantPump" && (start.X + start.Y + start.Z > 0))
            {
                start *= 3;
            }

            IMySlimBlock next = Grid.GetCubeBlock(position + start);

            if (CoolantCrawl(position, next, ref loop))
            {

                // There must be at least one pump active
                bool pass = false;
                for (int i = 0; i < loop.Count; i++) 
                {
                    if (CoolantPumpNames.Contains(loop[i].Block.BlockDefinition.Id.SubtypeId.ToString())) 
                    {
                        pass = true;
                        break;
                    }
                }

                if (!pass) return;

                ThermalLoopDefintion def = ThermalLoopDefintion.GetDefinition(ThermalLoopDefintion.DefaultLoopDefinitionId);
                ThermalLoop thermalLoop = new ThermalLoop(this, def, loop.ToArray());
                ThermalLoops.Add(thermalLoop);
            }
        }

        private bool CoolantCrawl(Vector3I last, IMySlimBlock b, ref List<ThermalCell> loop)
        {
            if (b == null) return false;

            if (b == loop[0].Block) return true;

            string subtype = b.BlockDefinition.Id.SubtypeId.ToString();
            if (!CoolantPipeLinkDirections.ContainsKey(subtype)) return false;

            Matrix m;
            b.Orientation.GetMatrix(out m);
            Vector3I[] directions = CoolantPipeLinkDirections[b.BlockDefinition.Id.SubtypeId.ToString()];
            for (int i = 0; i < directions.Length; i++) 
            {
                Vector3I dir;
                Vector3I.Transform(ref directions[i], ref m, out dir);

                if (b.BlockDefinition.Id.SubtypeId.ToString() == "Gauge_SG_CoolantPump" && (dir.X + dir.Y + dir.Z > 0))
                {
                    dir *=3;
                }

                Vector3I next = b.Min + dir;

                MyLog.Default.Info($"{b.BlockDefinition.Id.SubtypeId} [{next == last}] ({loop.Count}) | last: {last} --- next: {next} --- min: {b.Min + dir} --- max: {b.Max + dir}");

                if (next == last) continue;

                loop.Add(Get(b.Position));
                IMySlimBlock slim = b.CubeGrid.GetCubeBlock(next);
                return CoolantCrawl(b.Position, slim, ref loop);

            }

            MyLog.Default.Warning($"[{Settings.Name}] something went horribly wrong with the coolant crawler. please debug!");
            return false;
        }
    }
}