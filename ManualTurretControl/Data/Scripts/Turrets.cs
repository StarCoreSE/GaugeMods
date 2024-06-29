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
using VRage.Utils;

namespace Gauge.ManualTurret
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), false)]
    public class BulletTurret : Turrets
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret), false)]
    public class InteriorTurret : Turrets
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), false)]
    public class MissileTurret : Turrets
    {
    }

    public class Turrets : MyGameLogicComponent
    {
        private bool waitframe = true;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            MyLog.Default.Info($"[MTC] turret being initialized. CoreInit:{Core.initialized}");
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
            MyLog.Default.Info($"[MTC] turret update before frame. CoreInit:{Core.initialized}");
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
