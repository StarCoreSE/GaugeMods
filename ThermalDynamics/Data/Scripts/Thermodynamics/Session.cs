using Draygo.API;
using Draygo.BlockExtensionsAPI;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using SENetworkAPI;
using System;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Thermodynamics
{
	[MySessionComponentDescriptor(MyUpdateOrder.Simulation)]
	public class Session : MySessionComponentBase
	{

        public const ushort ModID = 30323;

        public static HudAPIv2 hudBase;
        public static HudAPIv2.HUDMessage hudStatusTool;
        public static HudAPIv2.HUDMessage hudStatusGrid;
        public static DefinitionExtensionsAPI Definitions;

        private static StringBuilder ToolText = new StringBuilder($"");
        private static StringBuilder GridText = new StringBuilder($"");

        public Session()
        {
            MyLog.Default.Info($"[{Settings.Name}] Setup Definition Extention API");
            Definitions = new DefinitionExtensionsAPI(Done);
        }

        private void Done()
        {
            MyLog.Default.Info($"[{Settings.Name}] Definition Extention API - Done");
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            NetworkAPI.Init(ModID, Settings.Name);
            NetworkAPI.LogNetworkTraffic = true;

            hudBase = new HudAPIv2(hudInit);
        }

        private void hudInit()
        {
            hudStatusTool = new HudAPIv2.HUDMessage(ToolText, new Vector2D(-1, 0), null, -1, 1, true, false, null, BlendTypeEnum.PostPP, "white");
            hudStatusTool.InitialColor = Color.White;
            hudStatusTool.ShadowColor = Color.White;
            hudStatusTool.Scale *= 1;
            hudStatusTool.Origin = new Vector2D(0.02f, 0.015f);
            hudStatusTool.Visible = true;

            hudStatusGrid = new HudAPIv2.HUDMessage(GridText, new Vector2D(-1, 0), null, -1, 1, true, false, null, BlendTypeEnum.PostPP, "white");
            hudStatusGrid.InitialColor = Color.White;
            hudStatusGrid.ShadowColor = Color.White;
            hudStatusGrid.Scale *= 1;
            hudStatusGrid.Origin = new Vector2D(0.75f, -0.45f);
            hudStatusGrid.Visible = true;

        }

        protected override void UnloadData()
        {
            Definitions?.UnloadData();

            base.UnloadData();
        }

        public override void Simulate()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            if (Settings.Instance.DebugTextOnScreen)
            {
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

                MyAPIGateway.Utilities.ShowNotification(
                    $"[Cell] {c.Block.Position} " +
                    $"T: {c.Temperature.ToString("n3")} " +
                    $"Gain: {(c.DeltaTemperature + c.HeatGeneration).ToString("n3")} " +
                    $"dT: {c.DeltaTemperature.ToString("n3")} " +
                    $"In: {c.HeatGeneration.ToString("n3")} " + 
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
                
                MyAPIGateway.Utilities.ShowNotification($"[Env] Ambiant: {g.FrameAmbientTemprature.ToString("n3")}", 1, "White");

                MyAPIGateway.Utilities.ShowNotification($"[Surface] {g.DebugSurfaceStateText(g.Surfaces[position])} ", 1, "White");

                StringBuilder sb = new StringBuilder();
                foreach (var loop in g.ThermalLoops) 
                {
                    sb.Append($"{loop.Loop.Length}-{loop.Temperature.ToString("n3")}, ");
                }
                    
                MyAPIGateway.Utilities.ShowNotification($"[Coolant] {sb.ToString()}", 1, "White");
            }
        }

        private bool UsingExtinguisherTool()
        {
            IMyCharacter character = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity as IMyCharacter;

            if (character == null || character.EquippedTool == null) return false;

            IMyAutomaticRifleGun extinguisher = character.EquippedTool as IMyAutomaticRifleGun;
            if (extinguisher != null && extinguisher.DefinitionId.SubtypeId.ToString() == "ExtinguisherGun") 
            {
                return true;
            }

            return false;
        }

        public override void Draw()
		{
            if (MyAPIGateway.Utilities.IsDedicated) return;

            DrawToolHud();
            DrawGridHud();
        }

        private void DrawToolHud() 
        {
            ToolText.Clear();
            if (UsingExtinguisherTool())
            {
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

                ToolText.Append($"{Tools.KelvinToCelsiusString(c.Temperature)}");
            }
        }

        private void DrawGridHud() 
        {
            GridText.Clear();
            IMyCubeBlock controlledBlock = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity as IMyCubeBlock;
            if (controlledBlock == null) return;

            MyCubeGrid grid = controlledBlock.CubeGrid as MyCubeGrid;
            if (grid == null) return;
            ThermalGrid tg = grid.GameLogic.GetAs<ThermalGrid>();
            if (tg == null) return;

            GridText.Append($"Ambient: {Tools.KelvinToCelsiusString(tg.FrameAmbientTemprature)}\n");

            if (tg.HottestBlock != null) 
            {
                if (hudStatusGrid != null) 
                {
                    hudStatusGrid.InitialColor = ColorExtensions.HSVtoColor(Tools.GetTemperatureColor(tg.HottestBlock.Temperature));
                }
                GridText.Append($"Peak Temp: {Tools.KelvinToCelsiusString(tg.HottestBlock.Temperature)}\n");
            }

            GridText.Append($"Critical Blocks: {tg.CriticalBlocks}\n");
            GridText.Append($"Coolant Loops: {tg.ThermalLoops.Count}\n");
        }

        public void DrawBillboard(ThermalCell c, MatrixD cameraMatrix)
        {
            Vector3D position;
            c.Block.ComputeWorldCenter(out position);

            float averageBlockLength = Vector3I.DistanceManhattan(c.Block.Max + 1, c.Block.Min) * 0.33f;

            Color color = ColorExtensions.HSVtoColor(Tools.GetTemperatureColor(c.Temperature));

            float distance = 0.01f;
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
