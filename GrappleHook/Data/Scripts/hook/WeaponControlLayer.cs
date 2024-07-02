using ProjectilesImproved;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SENetworkAPI;
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

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), true, "GrappleHookTurret")]
    public class WeaponControlLayer : MyGameLogicComponent
    {
        public static bool Hijack = false;

        private bool waitframe = true;

        private IMyLargeTurretBase Turret;
        private IMyGunObject<MyGunBase> gun;

        public enum States { idle, active, attached }
        private States State = States.idle;

        private Vector3 localGrapplePosition = Vector3.Zero;
        private Vector3I localGrapplePositionI = Vector3I.Zero;
        private IMyEntity connectedEntity = null;
        private NetSync<double> GrappleLength;

        private NetSync<ShootData> Shooting;
        private NetSync<AttachData> Attachment;
        private NetSync<bool> ResetIndicator;

        private Vector3D GrapplePosition = Vector3D.Zero;
        private Vector3 GrappleDirection = Vector3.Zero;


        private double k = 200000000;
        private float maxLength = 300;
        private float GrappleSpeed = 1.5f;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Shooting = new NetSync<ShootData>(this, TransferType.Both, new ShootData());
            Shooting.ValueChangedByNetwork += ShotFired;

            Attachment = new NetSync<AttachData>(this, TransferType.ServerToClient, new AttachData());
            Attachment.ValueChangedByNetwork += Attaching;

            ResetIndicator = new NetSync<bool>(this, TransferType.ServerToClient);
            ResetIndicator.ValueChanged += ResetCall;

            GrappleLength = new NetSync<double>(this, 0);

            gun = Entity as IMyGunObject<MyGunBase>;
            Turret = Entity as IMyLargeTurretBase;
            Turret.Range = 0;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            if (!Hijack)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        private void ResetCall(bool arg1, bool arg2)
        {
            Reset();
        }

        private void Attaching(AttachData data1, AttachData data2, ulong arg3)
        {
            connectedEntity = MyAPIGateway.Entities.GetEntityById(data2.entityId);
            localGrapplePosition = data2.localAttachmentPoint;
            localGrapplePositionI = data2.localAttachmentPointI;
            State = States.attached;
        }

        private void ShotFired(ShootData data1, ShootData data2, ulong steamId)
        {
            GrapplePosition = data2.position;
            GrappleDirection = data2.direction;
            State = States.active;
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
            switch (State)
            {
                case States.idle:
                    if (Turret.IsShooting) Shoot();
                    break;
                case States.active:
                    UpdateProjectile();
                    break;
                case States.attached:
                    ApplyGrapplingForce();
                    break;
            }
        }


        public override void UpdateAfterSimulation()
        {
            Draw();
        }

        private void ApplyGrapplingForce()
        {
            Vector3D turretPostion = gun.GetMuzzlePosition();
            Vector3D entityPostion = Vector3D.Transform(localGrapplePosition, connectedEntity.WorldMatrix);
            Vector3D direction = turretPostion - entityPostion;
            double currentLength = (direction).Length();
            direction.Normalize();

            double force = k * Math.Max(0, currentLength - GrappleLength.Value);

            Turret.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -1 * direction * force, turretPostion, null);
            connectedEntity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, direction * force, entityPostion, null);
        }

        private void UpdateProjectile()
        {
            Tools.Debug($"Projectile In Flight {(gun.GetMuzzlePosition() - GrapplePosition).Length()}");
            Vector3 delta = GrappleDirection * GrappleSpeed;

            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(GrapplePosition, GrapplePosition + delta, out hit);
            if (hit != null && !hit.HitEntity.MarkedForClose)
            {
                connectedEntity = hit.HitEntity;
                connectedEntity.OnMarkForClose += attachedEntityClosed;

                if (connectedEntity is IMyCubeGrid) 
                {
                    MyCubeGrid grid = (connectedEntity as MyCubeGrid);
                    grid.OnBlockRemoved += Grid_OnBlockRemoved;

                    Vector3I pos = grid.WorldToGridInteger(hit.Position + GrappleDirection * 0.1f);
                    IMySlimBlock block = grid.GetCubeBlock(pos);
                    if (block != null) 
                    {
                        localGrapplePositionI = block.Position;
                    }
                }

                localGrapplePosition = Vector3D.Transform(hit.Position + GrappleDirection * 0.1f, MatrixD.Invert(connectedEntity.WorldMatrix));
                GrappleLength.Value = (GrapplePosition - gun.GetMuzzlePosition()).Length() + 1f;
                State = States.attached;

                if (MyAPIGateway.Session.IsServer) 
                {
                    AttachData data = new AttachData();
                    data.entityId = hit.HitEntity.EntityId;
                    data.localAttachmentPoint = localGrapplePosition;
                    data.localAttachmentPointI = localGrapplePositionI;

                    Attachment.Value = data;
                }

                Tools.Debug($"Hit entity: {hit.HitEntity.DisplayName}");
                return;
            }

            GrapplePosition += delta;

            // if grapple length goes beyond max length
            if ((gun.GetMuzzlePosition() - GrapplePosition).LengthSquared() > maxLength * maxLength)
            {
                ResetIndicator.Value = !ResetIndicator.Value;
            }
        }

        private void Grid_OnBlockRemoved(IMySlimBlock block)
        {
            if (block.Position == localGrapplePositionI) 
            {
                ResetIndicator.Value = !ResetIndicator.Value;
            }
        }

        private void attachedEntityClosed(IMyEntity entity)
        {
            ResetIndicator.Value = !ResetIndicator.Value;
        }

        private void Shoot()
        {
            Tools.Debug("Shooting!");
            MatrixD muzzleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
            Vector3 direction = muzzleMatrix.Forward;
            Vector3D origin = muzzleMatrix.Translation;

            GrappleDirection = direction;
            GrapplePosition = origin;
            State = States.active;

            ShootData shoot = new ShootData();
            shoot.direction = GrappleDirection;
            shoot.position = GrapplePosition;

            Shooting.Value = shoot;
        }

        private void Reset()
        {
            Tools.Debug("Resetting!");
            GrapplePosition = Vector3D.Zero;
            GrappleDirection = Vector3D.Zero;

            connectedEntity = null;
            localGrapplePosition = Vector3D.Zero;
            State = States.idle;
        }

        private void Draw() 
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            Vector4 color = VRageMath.Color.DarkGray;
            MyStringId texture = MyStringId.GetOrCompute("cable");

            ExternalForceData planetForces = WorldPlanets.GetExternalForces(Turret.WorldMatrix.Translation);
            Vector3D sagDirection = planetForces.Gravity;

            if (sagDirection == Vector3D.Zero)
            {
                sagDirection = Turret.WorldMatrix.Down;
            }

            Vector3D gunPo = gun.GetMuzzlePosition();

            Vector3D position;
            if (State == States.active)
            {
                position = GrapplePosition;
                Vector3D[] points = ComputeCurvePoints(gunPo, position, sagDirection, Vector3D.Distance(gunPo, position)*1.005f);

                for (int i = 0; i < points.Length - 1; i++)
                {
                    Vector3D start = points[i];
                    Vector3D end = points[i + 1];

                    MySimpleObjectDraw.DrawLine(start, end, texture, ref color, 0.15f, BlendTypeEnum.Standard);
                }
            }
            else if (State == States.attached)
            {
                position = Vector3D.Transform(localGrapplePosition, connectedEntity.WorldMatrix);
                Vector3D[] points = ComputeCurvePoints(gunPo, position, sagDirection, GrappleLength.Value);

                for (int i = 0; i < points.Length - 1; i++)
                {
                    Vector3D start = points[i];
                    Vector3D end = points[i + 1];

                    MySimpleObjectDraw.DrawLine(start, end, texture, ref color, 0.15f, BlendTypeEnum.Standard);
                }
            }
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
                oldAction = a.Action;

                if (bannedActions.Contains(a.Id))
                {
                    DisableAction(a);
                }
                else if (a.Id == "Shoot") 
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {

                            layer.Shoot();
                        }
                        else 
                        {
                            oldAction?.Invoke(block);
                        }
                    };
                }
                else if (a.Id == "Shoot")
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.Shoot();
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };
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

        public Vector3D[] ComputeCurvePoints(Vector3D start, Vector3D end, Vector3D sagDirection, double referenceLength, int n = 30)
        {
            //returns a list of points in world space of length n. n must be equal or greater than 2
            //n = 3 will produce 2 line segments, n=2 will produce 1 line segment.
            if (n < 2) n = 2;

            double ropeLength = Vector3D.Distance(start, end);
            double sagAmount = Math.Max(0, referenceLength - ropeLength) * 0.5;

            //Tools.Debug($"rope: {ropeLength}, sag: {sagAmount}, ref: {referenceLength} direction: {sagDirection}");

            Vector3D[] result = new Vector3D[n];
            for (int i = 0; i < n; i++)
            {
                double u = (double)i / ((double)n - 1);
                double v = 1d - u;

                Vector3D newPt = start * v + end * u;
                if (sagAmount > 0)
                {
                    newPt += sagDirection * ComputeRopeSag(u) * sagAmount;
                }
                result[i] = newPt;
            }

            return result;
        }

        public double ComputeRopeSag(double x)
        {
            return -4 * x * x + 4 * x;
        }
    }
}