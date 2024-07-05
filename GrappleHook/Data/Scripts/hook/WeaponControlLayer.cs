using BlinkDrive;
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
using System.Drawing;
using System.Linq.Expressions;
using System.Net.Mail;
using System.Reflection.Emit;
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

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), true, "GrappleHookTurretLarge")]
    public class WeaponControlLayer : MyGameLogicComponent
    {
        private bool waitframe = true;
        public static bool Hijack = false;

        private IMyLargeTurretBase Turret;
        private IMyGunObject<MyGunBase> gun;

        public enum States { idle, reloading, projectile, attached }
        
        private NetSync<States> State;
        private NetSync<long> ConnectedEntityId;
        private IMyEntity ConnectedEntity = null;
        private NetSync<Vector3> LocalGrapplePosition;
        private NetSync<Vector3I> LocalGrapplePositionI;

        private NetSync<double> GrappleLength;

        private NetSync<ShootData> Shooting;
        private NetSync<bool> ResetIndicator;
        private NetSync<Settings> settings;
        private NetSync<float> Winch;

        private NetSync<float> ReloadTime;

        private Vector3D GrapplePosition = Vector3D.Zero;
        private Vector3 GrappleDirection = Vector3.Zero;

        private bool terminalShootOn = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (MyAPIGateway.Session.IsServer)
            {
                Settings.Init();
                settings = new NetSync<Settings>(this, TransferType.ServerToClient, Settings.Instance, true, false);
            }
            else
            {
                settings = new NetSync<Settings>(this, TransferType.ServerToClient, Settings.GetDefaults(), true, false);
            }

            State = new NetSync<States>(this, TransferType.ServerToClient, States.idle);

            ConnectedEntityId = new NetSync<long>(this, TransferType.ServerToClient, 0);
            ConnectedEntityId.ValueChangedByNetwork += AttemptConnect;

            LocalGrapplePosition = new NetSync<Vector3>(this, TransferType.ServerToClient, Vector3.Zero);
            LocalGrapplePositionI = new NetSync<Vector3I>(this, TransferType.ServerToClient, Vector3I.Zero);

            Shooting = new NetSync<ShootData>(this, TransferType.Both, new ShootData());
            Shooting.ValueChanged += Shoot;

            ResetIndicator = new NetSync<bool>(this, TransferType.ClientToServer, false);
            ResetIndicator.ValueChanged += Reset;

            GrappleLength = new NetSync<double>(this, TransferType.ServerToClient, 0);

            Winch = new NetSync<float>(this, TransferType.Both, 0);

            ReloadTime = new NetSync<float>(this, TransferType.ServerToClient, 0);

            gun = Entity as IMyGunObject<MyGunBase>;
            Turret = Entity as IMyLargeTurretBase;
            Turret.Range = 0;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            if (!Hijack)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        private void Reset(bool o, bool n)
        {
            Reset();
        }
        private void Reset()
        {
            GrapplePosition = Vector3D.Zero;
            GrappleDirection = Vector3D.Zero;

            if (ConnectedEntity != null)
                ConnectedEntity.OnMarkForClose -= attachedEntityClosed;
            if (ConnectedEntity is IMyCubeGrid)
                (ConnectedEntity as IMyCubeGrid).OnBlockRemoved -= Grid_OnBlockRemoved;

            ConnectedEntity = null;
            ConnectedEntityId.Value = 0;
            LocalGrapplePosition.Value = Vector3.Zero;
            LocalGrapplePositionI.Value = Vector3I.Zero;
            State.Value = States.reloading;

            Tools.Debug($"Reset - State Change: {State}");
        }

        private void AttemptConnect(long o, long n, ulong steamId) 
        {
            AttemptConnect();
        }

        private void AttemptConnect()
        {
            if (ConnectedEntityId.Value != 0 && ConnectedEntity == null)
            {
                ConnectedEntity = MyAPIGateway.Entities.GetEntityById(ConnectedEntityId.Value);
                if (ConnectedEntity != null) 
                {
                    ConnectedEntity.OnMarkForClose += attachedEntityClosed;
                }
            }
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
            CreateControls();

            Hijack = true;
        }

        public override void UpdateBeforeSimulation()
        {
            switch (State.Value)
            {
                case States.idle:
                    VerifyAndRequestShoot();
                    break;
                case States.reloading:
                    Reload();
                    break;
                case States.projectile:
                    UpdateProjectile();
                    break;
                case States.attached:
                    AttemptConnect();
                    UpdateLength();
                    ApplyForce();
                    UpdateZipLine();
                    break;
            }


        }

        public override void UpdateAfterSimulation()
        {
            Draw();
        }

        private void VerifyAndRequestShoot(bool terminalShoot = false)
        {
            if (State.Value == States.idle && (Turret.IsShooting || terminalShoot || terminalShootOn) && ReloadTime.Value <= 0)
            {
                Tools.Debug("Requesting Shoot");
                MatrixD muzzleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
                Vector3 direction = muzzleMatrix.Forward;
                Vector3D origin = Turret.PositionComp.WorldMatrixRef.Translation;

                ShootData shoot = new ShootData();
                shoot.direction = direction;
                shoot.position = origin;

                Shooting.Value = shoot;
            }
        }

        private void Shoot(ShootData o, ShootData n)
        {
            if (State.Value != States.attached && n.direction != Vector3.Zero)
            {
                GrapplePosition = Turret.PositionComp.WorldMatrixRef.Translation;
                GrappleDirection = n.direction;
                State.Value = States.projectile;
                ReloadTime.Value = (float)gun.GunBase.ReloadTime;
                Shooting.SetValue(new ShootData());
                Tools.Debug($"Shooting - State Change: {State.Value}");
            }
        }

        private void Reload()
        {
            if (ReloadTime.Value > 0)
                ReloadTime.SetValue( ReloadTime.Value-16.666667f);
            else
                State.Value = States.idle;
        }

        private void UpdateProjectile()
        {
            //Tools.Debug($"Projectile In Flight {(Turret.PositionComp.WorldMatrixRef.Translation - GrapplePosition).Length()}");
            Vector3 delta = GrappleDirection * settings.Value.GrappleProjectileSpeed;

            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(GrapplePosition, GrapplePosition + delta, out hit);
            if (hit != null && !hit.HitEntity.MarkedForClose)
            {
                ConnectedEntityId.Value = hit.HitEntity.EntityId;
                ConnectedEntity = hit.HitEntity;
                ConnectedEntity.OnMarkForClose += attachedEntityClosed;

                if (ConnectedEntity is IMyCubeGrid)
                {
                    MyCubeGrid grid = (ConnectedEntity as MyCubeGrid);
                    grid.OnBlockRemoved += Grid_OnBlockRemoved;

                    Vector3I pos = grid.WorldToGridInteger(hit.Position + GrappleDirection * 0.1f);
                    IMySlimBlock block = grid.GetCubeBlock(pos);
                    if (block != null)
                    {
                        LocalGrapplePositionI.Value = block.Position;
                    }
                }

                LocalGrapplePosition.Value = Vector3D.Transform(hit.Position + GrappleDirection * 0.1f, MatrixD.Invert(ConnectedEntity.WorldMatrix));
                GrappleLength.Value = (hit.Position - Turret.PositionComp.WorldMatrixRef.Translation).Length() + 1.25f;
                State.Value = States.attached;
                Tools.Debug($"Attached {hit.HitEntity.DisplayName} - State Change: {State.Value}");
            }
            else 
            {
                GrapplePosition += delta;

                // if grapple length goes beyond max length
                if ((Turret.PositionComp.WorldMatrixRef.Translation - GrapplePosition).LengthSquared() > settings.Value.ShootRopeLength * settings.Value.ShootRopeLength)
                {
                    ResetIndicator.Value = !ResetIndicator.Value;
                }
            }
        }

        private void UpdateLength()
        {
            float speed = 0;
            if (Winch.Value != 0)
                speed = Winch.Value * 0.0166667f;

            float speedAfterCheck = (float)Math.Max(Math.Min(GrappleLength.Value - speed, settings.Value.MaxRopeLength), settings.Value.MinRopeLength);
            if (speed != 0 && speedAfterCheck != GrappleLength.Value)
            {
                GrappleLength.SetValue(speedAfterCheck);
            }
        }

        public void ApplyForce()
        {
            try
            {
                if (Turret.CubeGrid.Physics == null || ConnectedEntity == null || ConnectedEntity.Physics == null || Entity.MarkedForClose || ConnectedEntity.MarkedForClose)
                {
                    return;
                }

                Vector3D turretPostion = Turret.PositionComp.WorldMatrixRef.Translation;
                Vector3D entityPostion = Vector3D.Transform(LocalGrapplePosition.Value, ConnectedEntity.WorldMatrix);
                Vector3D direction = turretPostion - entityPostion;
                double currentLength = direction.Length();
                direction.Normalize();

                double force = settings.Value.RopeForce * Math.Max(0, currentLength - GrappleLength.Value);

                if (force > 0 && turretPostion != Vector3D.Zero && entityPostion != Vector3D.Zero)
                {
                    Turret.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -1 * direction * force, turretPostion, null, null, true);
                    ConnectedEntity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, direction * force, entityPostion, null, null, true);
                }
            }
            catch(Exception e)
            {
                Tools.Debug(e.ToString());
            }
        }

        private void Grid_OnBlockRemoved(IMySlimBlock block)
        {
            if (block.Position == LocalGrapplePositionI.Value)
            {
                Reset();
            }
        }

        private void attachedEntityClosed(IMyEntity entity)
        {
            Reset();
        }

        private void UpdateZipLine()
        {
            //    Vector3D[] points = GetLinePoints();

            //    for (int i = 0; i < points.Length; i++) 
            //    {

            //    }
        }

        private void Draw()
        {
            try
            {
                if (MyAPIGateway.Utilities.IsDedicated) return;

                Vector4 color = VRageMath.Color.DarkGray;
                MyStringId texture = MyStringId.GetOrCompute("cable");

                Vector3D sagDirection = GetSagDirection();
                Vector3D gunPosition = Turret.PositionComp.WorldMatrixRef.Translation; //gun.GetMuzzlePosition();

                Vector3D position;
                if (State.Value == States.projectile)
                {
                    position = GrapplePosition;
                    Vector3D[] points = ComputeCurvePoints(gunPosition, position, sagDirection, Vector3D.Distance(gunPosition, position) * 1.005f, settings.Value.RopeSegments);

                    for (int i = 0; i < points.Length - 1; i++)
                    {
                        Vector3D start = points[i];
                        Vector3D end = points[i + 1];

                        MySimpleObjectDraw.DrawLine(start, end, texture, ref color, 0.15f, BlendTypeEnum.Standard);
                    }
                }
                else if (State.Value == States.attached)
                {
                    position = Vector3D.Transform(LocalGrapplePosition.Value, ConnectedEntity.WorldMatrix);
                    Vector3D[] points = ComputeCurvePoints(gunPosition, position, sagDirection, GrappleLength.Value, settings.Value.RopeSegments);

                    for (int i = 0; i < points.Length - 1; i++)
                    {
                        Vector3D start = points[i];
                        Vector3D end = points[i + 1];

                        MySimpleObjectDraw.DrawLine(start, end, texture, ref color, 0.15f, BlendTypeEnum.Standard);
                    }
                }
            }
            catch(Exception e) 
            {
                Tools.Debug(e.ToString());
            }
        }

        private Vector3D GetSagDirection()
        {
            ExternalForceData planetForces = WorldPlanets.GetExternalForces(Turret.PositionComp.WorldMatrixRef.Translation);
            Vector3D sagDirection = planetForces.Gravity;

            if (sagDirection == Vector3D.Zero)
            {
                sagDirection = Turret.WorldMatrix.Down;
            }

            return sagDirection;
        }

        public Vector3D[] GetLinePoints()
        {
            if (State.Value != States.attached) return new Vector3D[0];


            Vector3D gunPosition = Turret.PositionComp.WorldMatrixRef.Translation; //gun.GetMuzzlePosition();
            Vector3D position = Vector3D.Transform(LocalGrapplePosition.Value, ConnectedEntity.WorldMatrix);
            Vector3D sagDirection = GetSagDirection();
            return ComputeCurvePoints(gunPosition, position, sagDirection, GrappleLength.Value,settings.Value.RopeSegments);
        }

        public Vector3D[] ComputeCurvePoints(Vector3D start, Vector3D end, Vector3D sagDirection, double referenceLength, int n = 10)
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
                oldWriter = a.Writer;

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
                            layer.terminalShootOn = !layer.terminalShootOn;

                            if (layer.terminalShootOn)
                            {
                                layer.VerifyAndRequestShoot(true);
                            }
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };

                    a.Writer = (block, text) =>
                    {
                        WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (logic != null)
                        {
                            if (logic.terminalShootOn)
                            {
                                text.Append("On");
                            }
                            else
                            {
                                text.Append("Off");
                            }
                        }
                        else
                        {
                            oldWriter?.Invoke(block, text);
                        }
                    };
                }
                else if (a.Id == "Shoot_On")
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.terminalShootOn = true;
                            layer.VerifyAndRequestShoot(true);
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };

                    a.Writer = (block, text) =>
                    {
                        WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (logic != null)
                        {
                            if (logic.terminalShootOn)
                            {
                                text.Append("On");
                            }
                            else
                            {
                                text.Append("Off");
                            }
                        }
                        else
                        {
                            oldWriter?.Invoke(block, text);
                        }
                    };

                }
                else if (a.Id == "Shoot_Off")
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.terminalShootOn = false;
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };

                    a.Writer = (block, text) =>
                    {
                        WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (logic != null)
                        {
                            if (logic.terminalShootOn)
                            {
                                text.Append("On");
                            }
                            else
                            {
                                text.Append("Off");
                            }
                        }
                        else
                        {
                            oldWriter?.Invoke(block, text);
                        }
                    };
                }
                else if (a.Id == "ShootOnce")
                {
                    oldAction = a.Action;
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {

                            layer.VerifyAndRequestShoot(true);
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

                Tools.Debug($"{c.Id}");
                if (bannedControls.Contains(c.Id))
                {
                    HideControl(c);
                }
                else if (c.Id == "Shoot")
                {
                    IMyTerminalControlOnOffSwitch onoff = c as IMyTerminalControlOnOffSwitch;
                    oldGetter = onoff.Getter;
                    oldSetter = onoff.Setter;

                    onoff.Setter = (block, value) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.terminalShootOn = !layer.terminalShootOn;

                            if (layer.terminalShootOn)
                            {
                                layer.VerifyAndRequestShoot(true);
                            }
                        }
                        else
                        {
                            oldSetter?.Invoke(block, value);
                        }
                    };

                    onoff.Getter = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            return layer.terminalShootOn;
                        }
                        else
                        {
                            return oldGetter.Invoke(block);
                        }
                    };
                }
                else if (c.Id == "ShootOnce")
                {
                    IMyTerminalControlButton button = c as IMyTerminalControlButton;
                    oldAction = button.Action;
                    button.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.VerifyAndRequestShoot(true);
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };
                }
            }
        }

        private void CreateControls() 
        {
            Func<IMyTerminalBlock, bool> isThisMod = (block) => { return block.GameLogic.GetAs<WeaponControlLayer>() != null; };

            IMyTerminalAction detach = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("Detach");
            detach.Name = new StringBuilder("Detach");
            detach.Enabled = isThisMod;
            detach.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.ResetIndicator.Value = !logic.ResetIndicator.Value;
                }
            };
            detach.Writer = (block, text) => { text.Append("detach"); };


            IMyTerminalAction resetAction = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("ResetWinch");
            resetAction.Name = new StringBuilder("Reset");
            resetAction.Enabled = isThisMod;
            resetAction.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.Winch.Value = 0;
                }
            };
            resetAction.Writer = (block, text) => { text.Append("Reset"); };


            IMyTerminalAction tighten = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("TightenWinch");
            tighten.Name = new StringBuilder("Tighten");
            tighten.Enabled = isThisMod;
            tighten.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.Winch.Value = Math.Min(logic.Winch.Value + 1, logic.settings.Value.TightenSpeed);
                }
            };
            tighten.Writer = (block, text) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    text.Append($"{logic.Winch.Value.ToString("n0")}");
                }
            };


            IMyTerminalAction loosen = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("LoosenWinch");
            loosen.Name = new StringBuilder("Loosen");
            loosen.Enabled = isThisMod;
            loosen.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.Winch.Value = Math.Max(logic.Winch.Value - 1, -logic.settings.Value.LoosenSpeed);
                }
            };
            loosen.Writer = (block, text) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    text.Append($"{logic.Winch.Value.ToString("n0")}");
                }
            };


            IMyTerminalControlButton detachControl = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTerminalBlock>("Detach");
            detachControl.Title = MyStringId.GetOrCompute("Detach");
            detachControl.Tooltip = MyStringId.GetOrCompute("Breaks active connection");
            detachControl.Visible = isThisMod;
            detachControl.Enabled = isThisMod;
            detachControl.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.ResetIndicator.Value = !logic.ResetIndicator.Value;
                }
            };


            IMyTerminalControlSlider sliderWinch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("Winch");
            sliderWinch.Title = MyStringId.GetOrCompute("Tighten Winch");
            sliderWinch.Enabled = isThisMod;
            sliderWinch.Visible = isThisMod;
            sliderWinch.SetLimits(-settings.Value.LoosenSpeed, settings.Value.TightenSpeed);
            sliderWinch.Getter = (block) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    return layer.Winch.Value;
                }

                return 0;
            };
            sliderWinch.Setter = (block, value) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    layer.Winch.Value = value;
                }
            };
            sliderWinch.Writer = (block, builder) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    builder.Append($"{layer.Winch.Value.ToString("n2")}m/s");
                }
            };

            IMyTerminalControlButton resetWinch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTerminalBlock>("ResetWinch");
            resetWinch.Title = MyStringId.GetOrCompute("Reset Winch");
            resetWinch.Enabled = isThisMod;
            resetWinch.Visible = isThisMod;
            resetWinch.Action = (block) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    layer.Winch.Value = 0;
                    sliderWinch.UpdateVisual();
                }
            };

            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(detachControl);
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(resetWinch);
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(sliderWinch);

            MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(detach);
            MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(resetAction);
            MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(tighten);
            MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(loosen);

        }
    }
}