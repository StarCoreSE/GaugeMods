using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Input;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using Sandbox.Game.Entities;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;



namespace SpawnDamage
{
	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class Core : MySessionComponentBase
    {

        private float ArmorPercent = 0.5f;
        private float FunctionalPercent = 0.25f;
        
        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
            MyLog.Default.Info("Testing Test Test Testing!!!");
			MyAPIGateway.Entities.OnEntityAdd += GridAdded;
		}

		protected override void UnloadData()
		{
			MyAPIGateway.Entities.OnEntityAdd -= GridAdded;
		}

		private void GridAdded(IMyEntity ent)
		{
            
            //TODO add logic for tracking only registered grids

            MyLog.Default.Info($"Found Grid Entity: {ent is MyCubeGrid}");

			MyCubeGrid grid = ent as MyCubeGrid;
			if (grid == null || grid.Physics == null)
				return;

            (grid as IMyCubeGrid).GetBlocks(null, ApplyDamage);

            
		}

        private bool ApplyDamage(IMySlimBlock b)
        {
            if (b.FatBlock == null) 
            {
                // armor
                float damage = (b.Integrity-1) * ArmorPercent;

                if (damage > 0)
                {
                    b.DoDamage(damage, MyStringHash.GetOrCompute("gauge_magic"), true, null, 0, 0, false, null);
                }
            }
            else
            {
                // functional block
                MyCubeBlockDefinition d = b.BlockDefinition as MyCubeBlockDefinition;
                float min = (b.MaxIntegrity * d.CriticalIntegrityRatio) + 1;
                float damage = (b.Integrity - min) * FunctionalPercent;

                if (damage > 0)
                {
                    b.DoDamage(damage, MyStringHash.GetOrCompute("gauge_magic"), true, null, 0, 0, false, null);
                }
            }

            return false;
        }

    }
}