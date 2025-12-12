using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using System.Text;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public static class Debug
    {
        public static void ShowDebugInfo()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            if (Settings.Instance != null && Settings.Instance.DebugTextOnScreen)
            {
                MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;

                Vector3D start = matrix.Translation;
                Vector3D end = start + (matrix.Forward * 15);

                IHitInfo hit;
                MyAPIGateway.Physics.CastRay(start, end, out hit);
                MyCubeGrid grid = hit?.HitEntity as MyCubeGrid;

                if (grid == null) return;

                ThermalGrid g = grid.GameLogic.GetAs<ThermalGrid>();
                Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.005f));
                Vector3I inside = grid.WorldToGridInteger(matrix.Translation);
                IMySlimBlock block = grid.GetCubeBlock(position);

                if (block == null) return;

                ThermalCell c = g.Get(block.Position);

                if (c == null)
                    return;

                MyAPIGateway.Utilities.ShowNotification(
                    $"[Cell] {c.Block.Position} " +
                    $"T: {c.Temperature.ToString("n3")} " +
                    $"dT: {c.DeltaTemperature.ToString("n3")} " +
                    $"Gain: {c.HeatGeneration.ToString("n3")} " +
                    $"dC: {c.DeltaConvection.ToString("n3")} " +
                    $"dR: {c.DeltaRadiation.ToString("n3")} " +
                    $"ESA: {c.ExposedSurfaces.ToString("n0")} " +
                    $"", 1, "White");

                MyAPIGateway.Utilities.ShowNotification(
                    $"[Calc] m: {c.Mass.ToString("n0")} " +
                    $"k: {c.Definition.Conductivity} " +
                    $"sh {c.Definition.SpecificHeat} " +
                    $"em {c.Definition.Emissivity} " +
                    $"pwe: {c.Definition.ProducerWasteEnergy} " +
                    $"cwe: {c.Definition.ConsumerWasteEnergy} " +
                    $"tm: {(c.Definition.SpecificHeat * c.Mass).ToString("n0")} " +
                    $"c: {c.C.ToString("n4")} " +
                    $"prod: {c.EnergyProduction} " +
                    $"cons: {(c.EnergyConsumption + c.ThrustEnergyConsumption)} ", 1, "White");

                MyAPIGateway.Utilities.ShowNotification($"[Solar] Intensity: {c.IntensityDebug.ToString("n4")} Direction: {g.FrameSolarDirection.ToString("n3")}", 1, "White");

                MyAPIGateway.Utilities.ShowNotification($"[Room] eQ {g.ExternalQueue.Count} rQ {g.GridQueue.Count} ex: {g.Rooms[0].Count} nr: {g.Rooms[1].Count} rcnt: {g.Rooms.Count-2}", 1, "White");

                MyAPIGateway.Utilities.ShowNotification($"[Surface] {g.DebugSurfaceStateText(g.Surfaces[position])} ", 1, "White");

                //MyAPIGateway.Utilities.ShowNotification($"[Env] Ambiant: {g.FrameAmbientTemprature.ToString("n3")}", 1, "White");

                // StringBuilder sb = new StringBuilder();
                // foreach (var loop in g.ThermalLoops)
                // {
                //     sb.Append($"{loop.Loop.Length}-{loop.Temperature.ToString("n3")}, ");
                // }

                //MyAPIGateway.Utilities.ShowNotification($"[Coolant] {sb.ToString()}", 1, "White");
            }
        }
    }
}
