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

        private bool initialized = false;
        private bool initialized2 = false;

        private IMyTerminalAction controlAction;

        Action<IMyTerminalBlock> action;

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
                if (!initialized2)
                {
                    initialized2 = true;
                    return;
                }

                List<IMyTerminalAction> actions = new List<IMyTerminalAction>();
                MyAPIGateway.TerminalControls.GetActions<IMyLargeTurretBase>(out actions);

                List<IMyTerminalControl> controls = new List<IMyTerminalControl>();
                MyAPIGateway.TerminalControls.GetControls<IMyLargeTurretBase>(out controls);

                foreach (IMyTerminalAction action in actions)
                {
                    if (action.Id == "Control")
                    {
                        MyLog.Default.Info($"[MTC] found the initial action");
                        action.Enabled = bl => false;
                        this.action = action.Action;
                    }
                }

                foreach (IMyTerminalControl control in controls)
                {
                    if (control.Id == "Control")
                    {
                        MyLog.Default.Info($"[MTC] found the initial control");
                        control.Enabled = bl => false;
                    }
                }

                initialized = true;
            }


            tick++;
            if (turret != null && tick >= 11)
            {
                MyLog.Default.Info($"[MTC] entering turret after waiting for broadcasting to be turned on");
                EnterTurret(turret);
                turret = null;
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
                MyLog.Default.Info($"[MTC] Highlighting turret");
                highlightName = t.Name;
                Sandbox.Game.MyVisualScriptLogicProvider.SetHighlightLocal(highlightName, (int)environment.ContourHighlightThickness, 300, environment.ContourHighlightColor, playerId: MyAPIGateway.Session.Player.IdentityId);
            }
            else if (t.Name != highlightName)
            {
                MyLog.Default.Info($"[MTC] Canceling Highlighted turret");
                Sandbox.Game.MyVisualScriptLogicProvider.SetHighlightLocal(highlightName, -1, 300, environment.ContourHighlightColor, playerId: MyAPIGateway.Session.Player.IdentityId);
                highlightName = string.Empty;
            }

            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F))
            {
                MyLog.Default.Info($"[MTC] Interaction pressed. has_control_action:");
                StupidControllableEntity controller = PlayerConrollerEntity();
                isBroadcasting = controller.EnabledBroadcasting;

                if (!isBroadcasting)
                {
                    MyLog.Default.Info($"[MTC] turn on broadcasting");
                    controller.SwitchBroadcasting();
                    turret = t;
                    tick = 0;
                }

                EnterTurret(t);
            }
        }

        public void EnterTurret(IMyLargeTurretBase t)
        {
            MyLog.Default.Info($"[MTC] try to enter the turret");

            if (action == null)
            {
                MyLog.Default.Info($"[MTC] could not find the Control action");
                return;
            }

            action.Invoke(t);
        }



        public StupidControllableEntity PlayerConrollerEntity()
        {
            return ((StupidControllableEntity)MyAPIGateway.Session.Player.Controller.ControlledEntity);
        }

        public void CancelHighlight()
        {
            if (highlightName != string.Empty)
            {
                MyLog.Default.Info($"[MTC] Canceling Highlighted turret");
                Sandbox.Game.MyVisualScriptLogicProvider.SetHighlightLocal(highlightName, -1, 300, environment.ContourHighlightColor, playerId: MyAPIGateway.Session.Player.IdentityId);
                highlightName = string.Empty;
            }
        }
    }
}
