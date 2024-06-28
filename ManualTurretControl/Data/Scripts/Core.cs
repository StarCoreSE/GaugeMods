using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Interfaces;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.Input;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using StupidControllableEntity = Sandbox.Game.Entities.IMyControllableEntity;

namespace Gauge.ManualTurret
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Core : MySessionComponentBase
    {

        private string highlightName = string.Empty;
        private MyEnvironmentDefinition environment;

        private IMyTerminalAction controlAction;

        private bool initialized = false;
        private bool active = false;
        private bool isBroadcasting = false;

        private IMyLargeTurretBase turret;

        private int tick = 0;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            environment = MyDefinitionManager.Static.EnvironmentDefinition;
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated ||
                MyAPIGateway.Session.Player?.Character == null) return;

            if (!initialized) 
            {
                List<IMyTerminalAction> actions = new List<IMyTerminalAction>();
                MyAPIGateway.TerminalControls.GetActions<IMyLargeTurretBase>(out actions);

                foreach (IMyTerminalAction action in actions)
                {
                    if (action.Id == "Control")
                    {
                        controlAction = action;
                    }
                }
                initialized = true;
            }


            tick++;
            if (turret != null && tick >= 11) 
            {
                EnterTurret(turret);
                turret = null;
            }

            // this lets you exit without instantly re-entering the turret
            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F) && active) 
            {
                tick2 = 0;
                active = false;
                shutdown = true;
                return;
            }

            MatrixD playerMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(playerMatrix.Translation + playerMatrix.Forward * 0.1, playerMatrix.Translation + playerMatrix.Forward * 10, out hit);

            if (hit == null || !(hit.HitEntity is IMyCubeGrid))
            {
                CancelHighlight();
                return;
            }

            IMyCubeGrid grid = hit.HitEntity as IMyCubeGrid;

            if (grid == null || grid.MarkedForClose || grid.Physics == null)
            {
                CancelHighlight();
                return;
            }

            Vector3I pos = grid.WorldToGridInteger(hit.Position + playerMatrix.Forward * 0.1);
            IMySlimBlock b = grid.GetCubeBlock(pos);

            if (b == null || b.FatBlock == null || !(b.FatBlock is IMyLargeTurretBase))
            {
                CancelHighlight();
                return;
            }

            IMyLargeTurretBase t = b.FatBlock as IMyLargeTurretBase;

            if (highlightName == string.Empty)
            {
                highlightName = t.Name;
                Sandbox.Game.MyVisualScriptLogicProvider.SetHighlightLocal(highlightName, (int)environment.ContourHighlightThickness, 300, environment.ContourHighlightColor, playerId: MyAPIGateway.Session.Player.IdentityId);
            }
            else if (t.Name != highlightName)
            {
                Sandbox.Game.MyVisualScriptLogicProvider.SetHighlightLocal(highlightName, -1, 300, environment.ContourHighlightColor, playerId: MyAPIGateway.Session.Player.IdentityId);
                highlightName = string.Empty;
            }

            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F))
            {
                if (controlAction != null) 
                {

                    StupidControllableEntity controller = PlayerConrollerEntity();
                    isBroadcasting = controller.EnabledBroadcasting;

                    if (!isBroadcasting) 
                    {
                        controller.SwitchBroadcasting();
                        turret = t;
                        tick = 0;
                        return;
                    }

                    EnterTurret(t);
                    active = true;
                }
            }
        }

        public void EnterTurret(IMyLargeTurretBase t) 
        {
            controlAction.Enabled = block => true;
            controlAction.Apply(t);
            controlAction.Enabled = block => false;
        }

        public StupidControllableEntity PlayerConrollerEntity() 
        {
            return ((StupidControllableEntity)MyAPIGateway.Session.Player.Controller.ControlledEntity);
        }

        public void CancelHighlight()
        {
            if (highlightName != string.Empty)
            {
                Sandbox.Game.MyVisualScriptLogicProvider.SetHighlightLocal(highlightName, -1, 300, environment.ContourHighlightColor, playerId: MyAPIGateway.Session.Player.IdentityId);
                highlightName = string.Empty;
            }
        }
    }
}
