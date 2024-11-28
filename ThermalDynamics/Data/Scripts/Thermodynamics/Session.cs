using Draygo.BlockExtensionsAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
	[MySessionComponentDescriptor(MyUpdateOrder.Simulation)]
	public class Session : MySessionComponentBase
	{

        public static DefinitionExtensionsAPI Definitions;
        public Session()
        {
            MyLog.Default.Info($"[{Settings.Name}] Setup Definition Extention API");
            Definitions = new DefinitionExtensionsAPI(Done);
        }

        private void Done()
        {
            MyLog.Default.Info($"[{Settings.Name}] Definition Extention API - Done");
        }

        protected override void UnloadData()
        {
            Definitions?.UnloadData();

            base.UnloadData();
        }

        public Color GetTemperatureColor(float temp)
        {
            float max = 500f;
            // Clamp the temperature to the range 0-100
            float t = Math.Max(0, Math.Min(max, temp));



            // Calculate the red and blue values using a linear scale
            float red = (t / max);

            float blue = (1f - (t / max));

            return new Color(red, 0, blue, 255);
        }



        public override void Simulate()
        {
            if (!Settings.Debug || MyAPIGateway.Utilities.IsDedicated) return;
            //MyAPIGateway.Utilities.ShowNotification($"[Grid] Frequency: {Settings.Instance.Frequency}", 1, "White");
            MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;

            Vector3D start = matrix.Translation;
            Vector3D end = start + (matrix.Forward * 15);

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(start, end, out hit);
            MyCubeGrid grid = hit?.HitEntity as MyCubeGrid;

            if (grid == null) return;

            ThermalGrid g = grid.GameLogic.GetAs<ThermalGrid>();
            Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.005f));
            IMySlimBlock block = grid.GetCubeBlock(position);

            if (block == null) return;

            ThermalCell c = g.Get(block.Position);

            if (c == null)
                return;

            MyAPIGateway.Utilities.ShowNotification($"[Env] " +
                                                    $"sim: {Settings.Instance.SimulationSpeed.ToString("n2")} " +
                                                    $"freq: {Settings.Instance.Frequency.ToString("n2")} " +
                                                    $"tstep: {Settings.Instance.TimeScaleRatio.ToString("n2")} " +
                                                    $"ambT: {(g.FrameAmbientTemprature).ToString("n4")} " +
                                                    $"decay: {g.FrameSolarDecay.ToString("n4")} " +
                                                    $"wind: {g.FrameWindDirection.Length().ToString("n4")} " +
                                                    $"isOcc: {g.FrameSolarOccluded}", 1, "White");

            MyAPIGateway.Utilities.ShowNotification($"[Cell] {c.Block.Position} " +
                                                    $"T: {c.Temperature.ToString("n4")} " +
                                                    $"dT: {c.DeltaTemperature.ToString("n6")} " +
                                                    $"Gen: {c.HeatGeneration.ToString("n4")} " +
                                                    $"ext: {c.ExposedSurfaces.ToString("n4")} " +
                                                    $"kA: {string.Join(", ", c.kA)}", 1, "White");

            MyAPIGateway.Utilities.ShowNotification(
                $"[Calc] m: {c.Mass.ToString("n0")} " +
                $"k: {c.Definition.Conductivity} " +
                $"sh {c.Definition.SpecificHeat} " +
                $"em {c.Definition.Emissivity} " +
                $"pwe: {c.Definition.ProducerWasteEnergy} " +
                $"cwe: {c.Definition.ConsumerWasteEnergy} " +
                $"tm: {(c.Definition.SpecificHeat * c.Mass).ToString("n0")} " +
                $"c: {c.C.ToString("n4")} " +
                $"r: {c.Radiation.ToString("n2")} " +
                $"rdt: {(c.Radiation * c.ThermalMassInv).ToString("n4")} " +
                $"prod: {c.EnergyProduction} " +
                $"cons: {(c.EnergyConsumption + c.ThrustEnergyConsumption)} ", 1, "White");

            int value = g.NodeSurfaces[position];
            MyAPIGateway.Utilities.ShowNotification(
                $"[Grid] Exterior: {g.ExteriorNodes.Count} " +
                $"Nodes: {g.NodeSurfaces.Count} " +
                $"RNodes: {g.Rooms.Count} " +
                $"sq: {g.SolidQueue.Count} " +
                $"rq: {g.RoomQueue.Count} " +
                $"CrawlDone: {g.ThermalCellUpdateComplete} " +
                $"sbn: {string.Join(", ", c.TouchingSerfacesByNeighbor)}", 1, "White");

            MyAPIGateway.Utilities.ShowNotification(
                $"[Cell] Airtight out: {((value & 1 << 0) != 0 ? 1:0)}, {((value & 1 << 1) != 0 ? 1:0)}, {((value & 1 << 2) != 0?1:0)}, {((value & 1 << 3) != 0?1:0)}, {((value & 1 << 4) != 0?1:0)}, {((value & 1 << 5) != 0 ? 1 : 0)}, " +
                $"in: {((value & 1 << 6) != 0?1:0)}, {((value & 1 << 7) != 0 ? 1 : 0)}, {((value & 1 << 8) != 0 ? 1 : 0)}, {((value & 1 << 9) != 0 ? 1 : 0)}, {((value & 1 << 10) != 0?1:0)}, {((value & 1 << 11) != 0?1:0)}", 1, "White");
        }

        public override void Draw()
        {
            if (!Settings.Debug || MyAPIGateway.Utilities.IsDedicated) return;
            //MyAPIGateway.Utilities.ShowNotification($"[Grid] Frequency: {Settings.Instance.Frequency}", 1, "White");
            MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;

            Vector3D start = matrix.Translation;
            Vector3D end = start + (matrix.Forward * 15);

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(start, end, out hit);
            MyCubeGrid grid = hit?.HitEntity as MyCubeGrid;
            if (grid == null) return;

            Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.005f));

            ThermalGrid g = grid.GameLogic.GetAs<ThermalGrid>();

            IMySlimBlock block = grid.GetCubeBlock(position);

            if (block == null) return;

            ThermalCell c = g.Get(block.Position);

            if (c == null) return;

            DrawBillboard(c, matrix);
            for (int i = 0; i < c.Neighbors.Count; i++)
            {
                ThermalCell n = c.Neighbors[i];
                DrawBillboard(n, matrix);
            }
        }

        public void DrawBillboard(ThermalCell c, MatrixD cameraMatrix)
        {
            Vector3D position;
            c.Block.ComputeWorldCenter(out position);

            float averageBlockLength = Vector3I.DistanceManhattan(c.Block.Max + 1, c.Block.Min) * 0.33f;

            Color color = Tools.GetTemperatureColor(c.Temperature).HSVtoColor();

            const float distance = 0.01f;
            position = cameraMatrix.Translation + (position - cameraMatrix.Translation) * distance;
            float scaler = 1.2f * c.Grid.Grid.GridSizeHalf * averageBlockLength * distance;

            MyTransparentGeometry.AddBillboardOriented(
                MyStringId.GetOrCompute("GaugeThermalTexture"), // Texture or material name for the billboard
                color, // Color of the billboard
                position,
                cameraMatrix.Left, // Left direction of the billboard
                cameraMatrix.Up, // Up direction of the billboard
                scaler, // Width of the billboard
                scaler // Height of the billboard
            );
        }
    }
}
