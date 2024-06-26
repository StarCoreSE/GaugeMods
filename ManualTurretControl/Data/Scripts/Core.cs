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

namespace Gauge.ManualTurret
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Core : MySessionComponentBase
    {

        string highlightName = string.Empty;
        MyEnvironmentDefinition environment;


        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            environment = MyDefinitionManager.Static.EnvironmentDefinition;
        }

        public override void UpdateBeforeSimulation()
        {

            if (MyAPIGateway.Utilities.IsDedicated ||
                MyAPIGateway.Session.Player?.Character == null) return;

            MatrixD playerMatrix = MyAPIGateway.Session.Player.Character.GetHeadMatrix(true);
            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(playerMatrix.Translation + playerMatrix.Forward * 0.1, playerMatrix.Translation + playerMatrix.Forward * 10, out hit);

            if (hit == null || !(hit.HitEntity is IMyCubeGrid))
            {
                CancelHighlight();
                return;
            }
            MyAPIGateway.Utilities.ShowNotification($"[MTurret] hit grid", 1, "White");

            IMyCubeGrid grid = hit.HitEntity as IMyCubeGrid;

            if (grid == null || grid.MarkedForClose || grid.Physics == null)
            {
                CancelHighlight();
                return;
            }
            MyAPIGateway.Utilities.ShowNotification($"[MTurret] grid is real", 1, "White");

            Vector3I pos = grid.WorldToGridInteger(hit.Position + playerMatrix.Forward * 0.1);
            IMySlimBlock b = grid.GetCubeBlock(pos);

            if (b == null || b.FatBlock == null || !(b.FatBlock is IMyLargeTurretBase))
            {
                CancelHighlight();
                return;
            }
            MyAPIGateway.Utilities.ShowNotification($"[MTurret] found turret", 1, "White");

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

            MyAPIGateway.Utilities.ShowNotification($"[MTurret] is interface: {t is VRage.Game.ModAPI.Interfaces.IMyControllableEntity}, is entity: {t is Sandbox.Game.Entities.IMyControllableEntity}", 1, "White");

            if (MyAPIGateway.Input.IsNewKeyReleased(MyKeys.F))
            {
                MyAPIGateway.Session.Player.Controller.TakeControl(t as VRage.Game.ModAPI.Interfaces.IMyControllableEntity);
            }
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
