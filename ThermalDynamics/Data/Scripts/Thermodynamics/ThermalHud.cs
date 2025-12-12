using Draygo.API;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using System.Text;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static VRageRender.MyBillboard;
using Draygo.BlockExtensionsAPI;
using SENetworkAPI;
using VRage.Game.Components;
using VRage.Utils;

namespace Thermodynamics
{
    public static class ThermalHud
    {
        public static HudAPIv2 hudBase;
        public static HudAPIv2.HUDMessage hudStatusTool;
        public static HudAPIv2.HUDMessage hudStatusGrid;

        private static StringBuilder ToolText = new StringBuilder($"");
        private static StringBuilder GridText = new StringBuilder($"");

        public static void Initialize()
        {
            hudBase = new HudAPIv2(HudInit);
        }

        private static void HudInit()
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

        public static void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            DrawToolHud();
            DrawGridHud();
        }

        private static void DrawToolHud()
        {
            ToolText.Clear();
            if (UsingExtinguisherTool())
            {
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

        private static void DrawGridHud()
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
                GridText.Append($"Peak T: {Tools.KelvinToCelsiusString(tg.HottestBlock.Temperature)}\n");


                GridText.Append($"Peak dT: {((tg.HottestBlock.DeltaTemperature + tg.HottestBlock.HeatGeneration)*Settings.Instance.PerSecond).ToString("n3")}\n");
            }


            GridText.Append($"Critical Blocks: {tg.CriticalBlocks}\n");
            GridText.Append($"Coolant Loops: {tg.ThermalLoops.Count}\n");
        }

        private static bool UsingExtinguisherTool()
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

        public static void DrawBillboard(ThermalCell c, MatrixD cameraMatrix)
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
