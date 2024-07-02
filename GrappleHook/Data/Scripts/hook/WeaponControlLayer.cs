using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Weapons;
using Sandbox.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace GrappleHook
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), true, "LargeCalibreTurret")]
    /// <summary>
    /// Binds keens interface to the custom weapon types
    /// </summary>
    public class WeaponControlLayer : MyGameLogicComponent
    {
        public static bool Hijack = false;

        private bool waitframe = true;

        private IMyLargeTurretBase Turret;
        private IMyGunObject<MyGunBase> gun;

        private IMyEntity connectedEntity = null;
        private Vector3 localGrapplePosition = Vector3.Zero;
        private double GrappleLength;

        private bool GrappleActive = false;
        private bool GrappleAnchered => connectedEntity != null;

        private Vector3D GrapplePosition = Vector3D.Zero;
        private Vector3 GrappleDirection = Vector3.Zero;
        private float GrappleSpeed = 0.2f;

        private double k = 20000000;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Tools.Debug("intitializing");
            Tools.DebugMode = true;
            gun = Entity as IMyGunObject<MyGunBase>;

            Turret = Entity as IMyLargeTurretBase;
            Turret.Range = 0;



            if (Hijack) return;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            Tools.Debug("update before frame");
            if (Hijack) return;

            if (waitframe)
            {
                waitframe = false;
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }

            OverrideDefaultControls<IMyLargeTurretBase>();
            Hijack = true;
        }

        public override void UpdateBeforeSimulation()
        {
            if (Turret.IsShooting && !GrappleActive) 
            {
                Shoot();
            }

            if (!GrappleAnchered) 
            {
                UpdateProjectile();
            }
            else
            {
                Vector3D turretPostion = gun.GetMuzzlePosition();
                Vector3D entityPostion = Vector3D.Transform(localGrapplePosition, connectedEntity.WorldMatrix);
                Vector3D direction = turretPostion - entityPostion;
                double currentLength = (direction).Length();
                direction.Normalize();

                double force = k * Math.Max(0, currentLength - GrappleLength);

                Turret.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -1 * direction * force, turretPostion, null);
                connectedEntity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE,  direction * force, entityPostion, null);

                // apply grappling forces
            }
        }

        public override void UpdateAfterSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            Vector4 color = VRageMath.Color.DarkGray;
            MyStringId texture = MyStringId.GetOrCompute("cable");

            Vector3D position;
            if (!GrappleAnchered)
            {
                position = GrapplePosition;
                MySimpleObjectDraw.DrawLine(gun.GetMuzzlePosition(), position, texture, ref color, 0.05f, BlendTypeEnum.Standard);
            }
            else 
            {
                position = Vector3D.Transform(localGrapplePosition, connectedEntity.WorldMatrix);

                // add slack calculations
                MySimpleObjectDraw.DrawLine(gun.GetMuzzlePosition(), position, texture, ref color, 0.05f, BlendTypeEnum.Standard);
            }
        }

        private void UpdateProjectile()
        {
            if (!GrappleActive) return;

            Vector3 delta = GrappleDirection * GrappleSpeed;

            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(GrapplePosition, GrapplePosition + delta, out hit);
            if (hit != null && !hit.HitEntity.MarkedForClose)
            {

                connectedEntity = hit.HitEntity;
                localGrapplePosition = Vector3D.Transform(hit.Position, MatrixD.Invert(connectedEntity.WorldMatrix));
                GrappleLength = (hit.Position - gun.GetMuzzlePosition()).Length();
                Tools.Debug($"GrappleLength: {GrappleLength}");


                GrappleDirection = Vector3.Zero;
                GrapplePosition = Vector3D.Zero;



                Tools.Debug($"Hit entity: {hit.HitEntity.DisplayName}");
                return;

            }

            GrapplePosition += delta;
        }

        public void Shoot()
        {
            Tools.Debug("Takeing a shot!");
            Reset();
            MatrixD muzzleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
            Vector3 direction = muzzleMatrix.Forward;
            Vector3D origin = muzzleMatrix.Translation;

            GrappleDirection = direction;
            GrapplePosition = origin;
            GrappleActive = true;

        }

        public void Reset() 
        {
            GrappleActive = false;

            GrapplePosition = Vector3D.Zero;
            GrappleDirection = Vector3.Zero;

            connectedEntity = null;
            localGrapplePosition = Vector3D.Zero;
        }


        private static void DisableAction(IMyTerminalAction a)
        {
            Func<IMyTerminalBlock, bool> oldEnabled = a.Enabled;
            a.Enabled = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    return false;
                }

                return oldEnabled.Invoke(block);
            };
        }

        private static void HideControl(IMyTerminalControl a)
        {
            Func<IMyTerminalBlock, bool> oldVisiable = a.Visible;
            a.Visible = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    return false;
                }

                return oldVisiable.Invoke(block);
            };
        }

        private static void OverrideDefaultControls<T>()
        {
            Tools.Debug("Made it to override controls!");
            Action<IMyTerminalBlock> oldAction;
            Action<IMyTerminalBlock, StringBuilder> oldWriter;
            string[] bannedActions = new string[]
            {
                "IncreaseRange",
                "DecreaseRange",
                "EnableIdleMovement",
                "EnableIdleMovement_On",
                "EnableIdleMovement_Off",
                "TargetMeteors",
                "TargetMeteors_On",
                "TargetMeteors_Off",
                "TargetMissiles",
                "TargetMissiles_On",
                "TargetMissiles_Off",
                "TargetSmallShips",
                "TargetSmallShips_On",
                "TargetSmallShips_Off",
                "TargetLargeShips",
                "TargetLargeShips_On",
                "TargetLargeShips_Off",
                "TargetCharacters",
                "TargetCharacters_On",
                "TargetCharacters_Off",
                "TargetStations",
                "TargetStations_On",
                "TargetStations_Off",
                "TargetNeutrals",
                "TargetNeutrals_On",
                "TargetNeutrals_Off",
                "TargetFriends",
                "TargetFriends_On",
                "TargetFriends_Off",
                "TargetEnemies",
                "TargetEnemies_On",
                "TargetEnemies_Off",
                "EnableTargetLocking",
                "TargetingGroup_Weapons",
                "TargetingGroup_Propulsion",
                "TargetingGroup_PowerSystems",
                "TargetingGroup_CycleSubsystems",
                "FocusLockedTarget",
            };


            Func<IMyTerminalBlock, bool> oldGetter;
            Action<IMyTerminalBlock, bool> oldSetter;

            string[] bannedControls = new string[]
            {
                "Range",
                "EnableIdleMovement",
                "TargetMeteors",
                "TargetMissiles",
                "TargetSmallShips",
                "TargetLargeShips",
                "TargetCharacters",
                "TargetStations",
                "TargetNeutrals",
                "TargetFriends",
                "TargetEnemies",
                "EnableTargetLocking",
                "TargetingGroup_Selector",
                "FocusLockedTarget",
            };

            List<IMyTerminalAction> actions = new List<IMyTerminalAction>();
            MyAPIGateway.TerminalControls.GetActions<T>(out actions);
            foreach (IMyTerminalAction a in actions)
            {
                if (bannedActions.Contains(a.Id))
                {
                    DisableAction(a);
                }
            }

            List<IMyTerminalControl> controls = new List<IMyTerminalControl>();
            MyAPIGateway.TerminalControls.GetControls<T>(out controls);
            foreach (IMyTerminalControl c in controls)
            {
                if (bannedControls.Contains(c.Id))
                {
                    HideControl(c);
                }
            }
        }
    }
}