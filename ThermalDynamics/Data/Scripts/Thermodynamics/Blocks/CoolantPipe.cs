using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Thermodynamics
{

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeBlock), true, "Gauge_LG_CoolantPipe_Straight")]
    public class CoolantPipe : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            IMyCubeBlock block = (this.Entity as IMyCubeBlock);
            MyCubeBlockDefinition def = block.SlimBlock.BlockDefinition as MyCubeBlockDefinition;

            MyLog.Default.Info("TEST!!!!! " + def.MountPoints.Count().ToString());
        }
    }
}
