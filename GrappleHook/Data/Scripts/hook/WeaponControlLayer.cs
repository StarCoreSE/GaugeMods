using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
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

        private MyEntity Projectile;

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

        private NetSync<ZiplineEntity> RequestZiplineActivation;
        private NetSync<ZiplineEntity> RequestZiplineDisconnect;
        private NetSync<List<ZiplineEntity>> ZiplinePlayers;

        //private Vector3D GrapplePosition = Vector3D.Zero;
        //private Vector3 GrappleDirection = Vector3.Zero;

        private MatrixD GrappleMatrix = MatrixD.Zero;

        private bool terminalShootOn = false;
        private bool interactable = false;

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


            ZiplinePlayers = new NetSync<List<ZiplineEntity>>(this, TransferType.ServerToClient, new List<ZiplineEntity>());

            RequestZiplineActivation = new NetSync<ZiplineEntity>(this, TransferType.ClientToServer, new ZiplineEntity(), false);
            RequestZiplineActivation.ValueChanged += ZiplineActivation;

            RequestZiplineDisconnect = new NetSync<ZiplineEntity>(this, TransferType.ClientToServer, new ZiplineEntity(), false);
            RequestZiplineDisconnect.ValueChanged += ZiplineDisconnect;


            gun = Entity as IMyGunObject<MyGunBase>;
            Turret = Entity as IMyLargeTurretBase;
            Turret.Range = 0;

            Projectile = new MyEntity();
            Projectile.Init(null, ModContext.ModPath + "\\Models\\Ammo\\GrappleHookAmmo.mwm", null, null);

            MyEntities.Add(Projectile, true);

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
            HideProjectile();

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
            ValidateConnectedEntity();
        }

        private void ValidateConnectedEntity()
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

        public override void MarkForClose()
        {
            Projectile.Close();
        }

        public override void UpdateOnceBeforeFrame()
        {
            //Tools.Debug("update before frame");
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

                    MoveProjectileToTurretMuzzle();
                    VerifyAndRequestShoot();
                    break;
                case States.reloading:
                    Reload();
                    break;
                case States.projectile:
                    UpdateProjectile();
                    break;
                case States.attached:
                    ValidateConnectedEntity();
                    UpdateGrappleLength();
                    ApplyForce();
                    UpdateZiplineInteract();
                    UpdateZiplineForces();
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
                GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
                State.Value = States.projectile;
                ReloadTime.Value = (float)gun.GunBase.ReloadTime;
                Shooting.SetValue(new ShootData());
                Tools.Debug($"Shooting - State Change: {State.Value}");
            }
        }

        private void MoveProjectileToTurretMuzzle()
        {
            GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
            GrappleMatrix.Translation += GrappleMatrix.Forward * 1.1f;
            Projectile.WorldMatrix = GrappleMatrix;
        }

        private void HideProjectile()
        {
            GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
            GrappleMatrix.Translation = Vector3D.MaxValue;
            Projectile.WorldMatrix = GrappleMatrix;
        }
        private void Reload()
        {
            if (ReloadTime.Value > 0)
                ReloadTime.SetValue(ReloadTime.Value - 16.666667f);
            else
                State.Value = States.idle;
        }

        private void UpdateProjectile()
        {
            //Tools.Debug($"Projectile In Flight {(Turret.PositionComp.WorldMatrixRef.Translation - GrapplePosition).Length()}");
            Vector3 delta = GrappleMatrix.Forward * settings.Value.GrappleProjectileSpeed;

            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(GrappleMatrix.Translation, GrappleMatrix.Translation + delta, out hit);
            if (hit != null && !hit.HitEntity.MarkedForClose)
            {
                ConnectedEntityId.Value = hit.HitEntity.EntityId;
                ConnectedEntity = hit.HitEntity;
                ConnectedEntity.OnMarkForClose += attachedEntityClosed;

                if (ConnectedEntity is IMyCubeGrid)
                {
                    MyCubeGrid grid = (ConnectedEntity as MyCubeGrid);
                    grid.OnBlockRemoved += Grid_OnBlockRemoved;

                    Vector3I pos = grid.WorldToGridInteger(hit.Position + GrappleMatrix.Forward * 0.1f);
                    IMySlimBlock block = grid.GetCubeBlock(pos);
                    if (block != null)
                    {
                        LocalGrapplePositionI.Value = block.Position;
                    }
                }

                GrappleMatrix.Translation = hit.Position + GrappleMatrix.Forward * 0.1f;
                LocalGrapplePosition.Value = Vector3D.Transform(hit.Position + GrappleMatrix.Forward * 0.1f, MatrixD.Invert(ConnectedEntity.WorldMatrix));
                GrappleLength.Value = (hit.Position - Turret.PositionComp.WorldMatrixRef.Translation).Length() + 1.25f;
                State.Value = States.attached;
                Tools.Debug($"Attached {hit.HitEntity.DisplayName} - State Change: {State.Value}");
            }
            else
            {
                GrappleMatrix.Translation += delta;


                // if grapple length goes beyond max length
                if ((Turret.PositionComp.WorldMatrixRef.Translation - GrappleMatrix.Translation).LengthSquared() > settings.Value.ShootRopeLength * settings.Value.ShootRopeLength)
                {
                    ResetIndicator.Value = !ResetIndicator.Value;
                }
            }

            Projectile.WorldMatrix = GrappleMatrix;
        }

        private void UpdateGrappleLength()
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
            if (Turret.CubeGrid.Physics == null || ConnectedEntity == null || ConnectedEntity.Physics == null || Entity.MarkedForClose || ConnectedEntity.MarkedForClose)
            {
                return;
            }

            Vector3D turretPostion = Turret.PositionComp.WorldMatrixRef.Translation;
            Vector3D entityPostion = Vector3D.Transform(LocalGrapplePosition.Value, ConnectedEntity.WorldMatrix);
            Vector3D direction = turretPostion - entityPostion;
            double currentLength = direction.Length();
            direction = direction / currentLength;

            double force = settings.Value.RopeForce * (currentLength - GrappleLength.Value) - (settings.Value.RopeDamping * (Turret.CubeGrid.Physics.LinearVelocity + ConnectedEntity.Physics.LinearVelocity).Length());
            Vector3D forceVector = direction * force;

            if (force > 0)
            {
                Turret.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -1 * forceVector, turretPostion, null, null, true);
                ConnectedEntity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, forceVector, entityPostion, null, null, true);
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

        private void UpdateZiplineInteract()
        {
            interactable = false;

            bool interactKeyPressed = MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F);

            if (MyAPIGateway.Session.Player == null || !(MyAPIGateway.Session.Player.Controller.ControlledEntity is IMyCharacter)) return;

            ZiplineEntity e;
            if ((e = GetZiplineEntity(MyAPIGateway.Session.Player.IdentityId)) != null)
            {
                if (interactKeyPressed)
                {
                    RequestZiplineDisconnect.Value = e;
                }
                return;
            }

            IMyCamera cam = MyAPIGateway.Session.Camera;
            if (cam == null) return;

            RayD camRay = new RayD(cam.WorldMatrix.Translation, cam.WorldMatrix.Forward);
            Vector3D[] points = GetLinePoints();

            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3D a = points[i];
                Vector3D b = points[i + 1];

                MatrixD worldMatrix = Turret.WorldMatrix;
                BoundingBoxD bounds = new BoundingBoxD(Vector3D.Min(a, b), Vector3D.Max(a, b));

                double? result;
                bounds.Intersects(ref camRay, out result);
                if (result.HasValue && result.Value < 3.5f)
                {
                    interactable = true;
                    break;
                }
            }

            if (interactKeyPressed && interactable)
            {
                Vector3D ziplineDirection = Vector3D.Transform(LocalGrapplePosition.Value, ConnectedEntity.WorldMatrix) - Turret.WorldMatrix.Translation;

                bool direction = Vector3D.Dot(ziplineDirection, cam.WorldMatrix.Forward) >= 0;

                RequestZiplineActivation.Value = new ZiplineEntity(MyAPIGateway.Session.Player.IdentityId, direction);
            }
        }

        private void AttachToZipline(ZiplineEntity ziplineData)
        {
            Tools.Debug($"Attaching to zipline");
            ZiplineEntity.Populate(ref ziplineData);

            if (ziplineData.player == null || !(ziplineData.player.Controller.ControlledEntity is IMyCharacter))
            {
                Tools.Info($"Warning could not find the player in the list of players. This should never be happening!");
                return;
            }

            IMyCharacter character = ziplineData.player.Character;
            Vector3D[] points = GetLinePoints();

            Vector3D centerSegment = Vector3D.Zero;
            Vector3D nextSegment = Vector3D.Zero;
            Vector3D previousSegment = Vector3D.Zero;

            int nearIndex = 0;
            double nearValue = double.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                // iterates from end to start if the character is facing twards the turret
                int index = (ziplineData.direction) ? i : points.Length - 1 - i;

                Vector3D point = points[index];

                double distance = (character.WorldMatrix.Translation - point).LengthSquared();
                if (distance < nearValue)
                {
                    nearIndex = index;
                    nearValue = distance;
                    centerSegment = point;
                }
            }

            // Correctly assign next and previous segments based on nearIndex
            if (nearIndex + 1 < points.Length)
            {
                nextSegment = points[nearIndex + 1];
            }
            else
            {
                nextSegment = centerSegment;
            }

            if (nearIndex - 1 >= 0)
            {
                previousSegment = points[nearIndex - 1];
            }
            else
            {
                previousSegment = centerSegment;
            }

            // If direction is false, swap next and previous segments
            if (!ziplineData.direction)
            {
                Vector3D temp = nextSegment;
                nextSegment = previousSegment;
                previousSegment = temp;
            }

            Vector3D pulley;
            Vector3D anchor;
            Vector3D anchorNorm;
            double sinTheta;

            Vector3D characterPosition = character.WorldMatrix.Translation + character.WorldMatrix.Up;
            Vector3D centerToNext = nextSegment - centerSegment;
            Vector3D playerToCenter = centerSegment - characterPosition;
            double playerToCenterMag = playerToCenter.Length();

            // check if we are infront or behind the center point.
            double directionDot = Vector3D.Dot(playerToCenter, centerToNext);

            if (directionDot < 0)
            {
                // infront uses the next segment point as the anchor
                anchor = centerToNext;
                anchorNorm = Vector3D.Normalize(anchor);
                sinTheta = Tools.GetSinAngle(anchor, -playerToCenter);
            }
            else 
            {
                // behind uses the center segment point as the anchor
                anchor = centerSegment - previousSegment;
                anchorNorm = -Vector3D.Normalize(anchor);
                sinTheta = Tools.GetSinAngle(anchor, playerToCenter);

            }

            double playerPulleyMag = sinTheta * playerToCenterMag;
            double pulleyToAnchorMag = Math.Sqrt(playerToCenterMag * playerToCenterMag - playerPulleyMag * playerPulleyMag);
            pulley = centerSegment + anchorNorm * pulleyToAnchorMag;

            Tools.Debug($"Got pulley location {pulley}");

            ziplineData.pulley = pulley;
            ziplineData.lastPulley = pulley;
        }




        private void UpdateZiplineForces()
        {
            Vector3D[] points = GetLinePoints();
            for (int i = 0; i < ZiplinePlayers.Value.Count; i++)
            {
                ZiplineEntity zipEntity = ZiplinePlayers.Value[i];
                ZiplineEntity.Populate(ref zipEntity);

                if (zipEntity.player == null || !(zipEntity.player.Controller.ControlledEntity is IMyCharacter)) return;

                IMyCharacter character = zipEntity.player.Character;

                if (character == null) return;

                if (character.IsDead)
                {
                    ZiplinePlayers.Value.Remove(zipEntity);
                    ZiplinePlayers.Push();
                }

                Vector3D characterPosition = character.WorldMatrix.Translation + character.WorldMatrix.Up * 1.7f;
                Vector3D tetherVector = zipEntity.pulley - characterPosition;
                double tetherLength = tetherVector.Length();
                Vector3D tetherDirectionNorm = tetherVector / tetherLength;

                double force = settings.Value.ZiplineTetherForce * (tetherLength - settings.Value.ZiplineTetherLength);
                Vector3D forceVector = tetherDirectionNorm * force - settings.Value.ZiplineDamping * character.Physics.LinearVelocity;
                if (force > 0)
                {
                    character.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, forceVector, characterPosition, null, null, true);
                }

                double v = zipEntity.pulleyVelocity;
                double v0 = zipEntity.lastPulleyVelocity;

                Vector3D deltaVector = (zipEntity.pulley - zipEntity.lastPulley);
                double xDelta = deltaVector.Length();

                double g = settings.Value.ZiplineGraveForce;
                double a = 0;
                if (deltaVector != Vector3D.Zero)
                {
                    a = Vector3D.Dot(-tetherDirectionNorm, deltaVector.Normalized()) * g;
                }

                double min = settings.Value.ZiplinePulleyMinSpeed * 0.01666667f;

                double velocityCalc = v0 * v0 + 2d * a * xDelta * xDelta;

                double velocitySquared = Math.Max(velocityCalc, min * min);
                double velocityToApply = Math.Sqrt(velocitySquared);


                zipEntity.lastPulley = zipEntity.pulley;
                zipEntity.lastPulleyVelocity = zipEntity.pulleyVelocity;
                zipEntity.pulleyVelocity = velocityToApply;


                double distanceRemaining = velocityToApply;
                for (int j = 0; j < points.Length - 1; j++)
                {
                    if (distanceRemaining == 0) break;

                    int index = j;
                    int index2 = j + 1;
                    if (!zipEntity.direction)
                    {
                        index = points.Length - 1 - j;
                        index2 = index - 1;
                    }

                    Vector3D start = points[index];
                    Vector3D end = points[index2];

                    Vector3D segmentDiretion = end - start;
                    Vector3D segmentNorm = segmentDiretion.Normalized();

                    Vector3D startDirection = start - zipEntity.pulley;
                    Vector3D startNorm = Vector3D.Zero;
                    if (startDirection != Vector3D.Zero)
                    {
                        startNorm = startDirection.Normalized();
                    }

                    Vector3D endDirection = end - zipEntity.pulley;
                    Vector3D endNorm = endDirection.Normalized();


                    if ((Vector3D.Dot(startNorm, segmentNorm) <= 0 || Math.Abs((zipEntity.pulley - startDirection).Length()) < 0.01f) && Vector3D.Dot(endNorm, segmentNorm) >= 0)
                    {
                        double length = endDirection.Length();
                        Vector3D segmentDirectionNorm = endDirection.Normalized();

                        Vector3D addition;
                        if (distanceRemaining < length)
                        {
                            addition = segmentDirectionNorm * distanceRemaining;
                            distanceRemaining = 0;
                        }
                        else
                        {
                            distanceRemaining -= length;
                            addition = segmentDirectionNorm * length;
                        }

                        zipEntity.pulley += addition;
                    }
                }

                //Tools.Debug($"pulley move: {zipEntity.pulley}");

                if (distanceRemaining > 0)
                {
                    ZiplinePlayers.Value.Remove(zipEntity);
                    ZiplinePlayers.Push(SyncType.Broadcast);
                }

            }
        }

        private bool ZiplineContainsPlayer(IMyPlayer player)
        {
            return ZiplineContainsPlayer(player.IdentityId);
        }

        private bool ZiplineContainsPlayer(long playerId)
        {
            for (int i = 0; i < ZiplinePlayers.Value.Count; i++)
            {
                ZiplineEntity ent = ZiplinePlayers.Value[i];

                if (ent.playerId == playerId) return true;
            }
            return false;
        }

        private ZiplineEntity GetZiplineEntity(long playerId)
        {
            for (int i = 0; i < ZiplinePlayers.Value.Count; i++)
            {
                ZiplineEntity ent = ZiplinePlayers.Value[i];

                if (ent.playerId == playerId) return ent;
            }
            return null;
        }

        private void ZiplineActivation(ZiplineEntity o, ZiplineEntity n)
        {
            if (!ZiplineContainsPlayer(n.playerId))
            {
                AttachToZipline(n);
                ZiplinePlayers.Value.Add(n);
                ZiplinePlayers.Push(SyncType.Broadcast);
                Tools.Debug($"Player {n.playerId} activated the zipline. Active zipliners {ZiplinePlayers.Value.Count}");
            }
            else
            {
                Tools.Debug($"Player {n.playerId} is already active!");
            }
        }

        private void ZiplineDisconnect(ZiplineEntity o, ZiplineEntity n)
        {
            ZiplineEntity e = GetZiplineEntity(n.playerId);
            if (e != null)
            {
                ZiplinePlayers.Value.Remove(e);
                ZiplinePlayers.Push();
                Tools.Debug($"Player {n.playerId} was removed from the zipline. Active zipliners {ZiplinePlayers.Value.Count}");
            }
            else
            {
                Tools.Debug($"Player {n.playerId} does not exist in the list!");
            }
        }

        private void Draw()
        {
            try
            {
                if (MyAPIGateway.Utilities.IsDedicated) return;

                VRageMath.Vector4 color = VRageMath.Color.DarkGray;
                MyStringId texture;
                if (interactable)
                {
                    color = VRageMath.Color.Red;
                    texture = MyStringId.GetOrCompute("cableRed");

                }
                else
                {
                    color = VRageMath.Color.DarkGray;
                    texture = MyStringId.GetOrCompute("cable");
                }

                Vector3D sagDirection = GetSegmentDirection();
                Vector3D gunPosition = Turret.PositionComp.WorldMatrixRef.Translation;

                Vector3D position;
                if (State.Value == States.projectile)
                {
                    position = GrappleMatrix.Translation;
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


                for (int i = 0; i < ZiplinePlayers.Value.Count; i++)
                {
                    ZiplineEntity zipEntity = ZiplinePlayers.Value[i];
                    ZiplineEntity.Populate(ref zipEntity);

                    if (zipEntity.player == null || !(zipEntity.player.Controller.ControlledEntity is IMyCharacter)) return;

                    IMyCharacter character = zipEntity.player.Character;
                    if (character == null) return;

                    MySimpleObjectDraw.DrawLine(character.WorldMatrix.Translation + character.WorldMatrix.Up * 1.85f, zipEntity.pulley, texture, ref color, 0.05f, BlendTypeEnum.Standard);
                }

            }
            catch (Exception e)
            {
                Tools.Debug(e.ToString());
            }
        }

        private Vector3D GetSegmentDirection()
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
            try
            {
                if (State.Value != States.attached || ConnectedEntity == null) return new Vector3D[0];

                Vector3D gunPosition = Turret.WorldMatrix.Translation;
                Vector3D position = Vector3D.Transform(LocalGrapplePosition.Value, ConnectedEntity.WorldMatrix);
                Vector3D sagDirection = GetSegmentDirection();
                return ComputeCurvePoints(gunPosition, position, sagDirection, GrappleLength.Value, settings.Value.RopeSegments);
            }
            catch
            {
                return new Vector3D[0];
            }
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
                    newPt += sagDirection * ComputeRopeSegment(u) * sagAmount;
                }
                result[i] = newPt;
            }

            return result;
        }

        public double ComputeRopeSegment(double x)
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