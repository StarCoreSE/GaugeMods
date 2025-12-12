using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.Utils;
using SpaceEngineers.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.Components.Interfaces;
using VRage.Game.Components;

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {
        public override void UpdateBeforeSimulation()
        {
            FrameCount++;
            //MyAPIGateway.Utilities.ShowNotification($"[Loop] f: {MyAPIGateway.Session.GameplayFrameCounter} fc: {FrameCount} sf: {SimulationFrame} sq: {SimulationQuota}", 1, "White");

            GridMapperUpdate();

            // if you are done processing the required blocks this second
            // wait for the start of the next second interval
            if (SimulationQuota == 0)
            {
                if (FrameCount >= 60)
                {
                    SimulationQuota = GetSimulationQuota();
                    FrameCount = 0;
                    FrameQuota = 0;
                }
                else
                {
                    return;
                }
            }

            FrameQuota += GetFrameQuota();
            int cellCount = Thermals.Count;

            //MyAPIGateway.Utilities.ShowNotification($"[Loop] c: {count} frameC: {QuotaPerSecond} simC: {60f * QuotaPerSecond}", 1, "White");

            //Stopwatch sw = Stopwatch.StartNew();
            while (FrameQuota >= 1 || SimulationQuota == 0)
            {
                // prepare for the next simulation after a full iteration
                if (SimulationIndex == cellCount || SimulationIndex == -1)
                {

                    foreach (ThermalLoop loop in ThermalLoops)
                    {
                        loop.Update();
                    }

                    // start a new simulation frame
                    SimulationFrame++;
                    PrepareNextSimulationStep();

                    // reverse the index direction
                    Direction *= -1;
                    // make sure the end cells in the list go once per frame
                    SimulationIndex += Direction;
                }

                // Calculate how many cells to process this frame
                int cellsToProcess = Math.Min((int)FrameQuota, SimulationQuota);
                if (cellsToProcess <= 0) break;

                // Process cells sequentially (parallel processing not available in Space Engineers)
                ProcessCellsSequentially(cellsToProcess);

                FrameQuota -= cellsToProcess;
                SimulationQuota -= cellsToProcess;
            }

            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [UpdateLoop] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");

        }

        private void ProcessCellsSequentially(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (SimulationIndex < 0 || SimulationIndex >= Thermals.Count)
                {
                    SimulationIndex = Direction > 0 ? 0 : Thermals.Count - 1;
                }

                ThermalCell cell = Thermals.Cells[SimulationIndex];
                if (cell != null)
                {
                    if (SurfaceUpdateFrame == SimulationFrame)
                    {
                        cell.UpdateSurfaces();
                    }

                    cell.Update();

                    if (HottestBlock == null || cell.Temperature > HottestBlock.Temperature)
                    {
                        HottestBlock = cell;
                    }
                }

                SimulationIndex += Direction;
            }
        }

        /// <summary>
        /// Calculates thermal cell count second to match the desired simulation speed
        /// </summary>
        public int GetSimulationQuota()
        {
            return Math.Max(1, (int)(Thermals.Count * Settings.Instance.SimulationSpeed * Settings.Instance.Frequency));
        }

        /// <summary>
        /// Calculates the thermal cell count required each frame
        /// </summary>
        public float GetFrameQuota()
        {
            return 0.00000001f + ((Thermals.Count * Settings.Instance.SimulationSpeed * Settings.Instance.Frequency) / 60f);
        }
    }
}
