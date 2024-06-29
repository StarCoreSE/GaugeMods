using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.Game.WorldEnvironment.Modules;

namespace Gauge.ManualTurret
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), true)]
    public class BulletTurret : Turrets
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret), true)]
    public class InteriorTurret : Turrets
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), true)]
    public class MissileTurret : Turrets
    {
    }

    public class Turrets : MyGameLogicComponent
    {
        private bool waitframe = true;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (Core.initialized)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
            }
            else 
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Core.initialized) 
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            if (waitframe)
            {
                waitframe = false;
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }

            Core.Initialize();

        }
    }
}
