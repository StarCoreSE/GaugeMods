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

namespace Gauge.ManualTurret
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), false)]
    public class BulletTurret : TurretControls
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret), false)]
    public class InteriorTurret : TurretControls
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), false)]
    public class MissileTurret : TurretControls
    {
    }

    public class TurretControls : MyGameLogicComponent
    {
        private bool waitframe = true;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {


            if (waitframe)
            {
                waitframe = false;
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }

            Highjack<IMyLargeTurretBase>();

        }

        public void Highjack<T>()
        {
            List<IMyTerminalAction> actions = new List<IMyTerminalAction>();
            MyAPIGateway.TerminalControls.GetActions<T>(out actions);

            List<IMyTerminalControl> controls = new List<IMyTerminalControl>();
            MyAPIGateway.TerminalControls.GetControls<T>(out controls);

            foreach (IMyTerminalAction action in actions)
            {
                if (action.Id == "Control")
                {
                    action.Enabled = b => false;
                }
            }

            foreach (IMyTerminalControl control in controls)
            {
                if (control.Id == "Control")
                {
                    control.Enabled = b => false;
                }
            }
        }
    }
}
