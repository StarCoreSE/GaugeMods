using VRageMath;
using VRage.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using static VRageRender.MyBillboard;
using VRage.ObjectBuilders;
using Sandbox.ModAPI;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;


namespace PodracerCoupling
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "PodRacerCoupling")]
    public class CouplingBlock : MyGameLogicComponent
    {
        public int ConnectionRange { get; } = 10;

        public float EngineOffset { get; } = 3;

        public float DepthSpringForce { get; } = 15000;

        public float VirticalSpringForce { get; } = 7500;

        public float HorizontalSpringForce { get; } = 80000;


        public CouplingBlock LinkedBlock { get; private set; }

        private Base6Directions.Direction up;
        private Base6Directions.Direction left;
        private Base6Directions.Direction forward;


        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;
        }


        public override void MarkForClose()
        {
            base.MarkForClose();
            Unlink();
        }

        public override void Close()
        {
            base.Close();
            Unlink();
        }

        public override void UpdateBeforeSimulation()
        {
            Vector4 colorR = Color.Red;
            Vector4 colorG = Color.Green;
            Vector4 colorY = Color.Yellow;

            IMyCubeBlock block = (Entity as IMyCubeBlock);
            Vector3D start = block.WorldMatrix.Translation;
            Vector3D end = start + (block.WorldMatrix.Up * ConnectionRange);


            if (LinkedBlock != null)
            {
                Vector3D range = start + (block.WorldMatrix.Up * EngineOffset);
                MySimpleObjectDraw.DrawLine(start, range, MyStringId.GetOrCompute("cable"), ref colorG, 0.15f, BlendTypeEnum.Standard);
                DoKlang();
            }
            else
            {
                Vector4 color2 = Color.Purple;
                Vector3D end2 = start + (block.WorldMatrix.Forward * 10);
                MySimpleObjectDraw.DrawLine(start, end2, MyStringId.GetOrCompute("cable"), ref color2, 0.05f, BlendTypeEnum.Standard);

                CouplingBlock linkable = SearchForLinkable();

                if (linkable != null)
                {
                    if (ValidateLink(linkable))
                    {
                        Link(linkable);
                        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("cable"), ref colorR, 0.15f, BlendTypeEnum.Standard);
                    }
                    else
                    {
                        MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("cable"), ref colorY, 0.15f, BlendTypeEnum.Standard);
                    }
                }
                else
                {
                    MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("cable"), ref colorR, 0.15f, BlendTypeEnum.Standard);
                }
            }
        }




        private void Link(CouplingBlock block)
        {
            if (ValidateLink(block))
            {
                LinkedBlock = block;
                Matrix a = (Entity as IMyCubeBlock).CubeGrid.WorldMatrix;
                Matrix b = (block.Entity as IMyCubeBlock).CubeGrid.WorldMatrix;

                up = b.GetClosestDirection(a.Up);
                left = b.GetClosestDirection(a.Left);
                forward = b.GetClosestDirection(a.Forward);

                block.Link(this);
            }

        }

        public void Unlink()
        {
            if (LinkedBlock == null)
            {
                MyLog.Default.Warning("[CouplingBlock.Unlink] Unlink was called when no link existed. LinkedBlock was already null.");
                return;
            }

            CouplingBlock temp = LinkedBlock;
            LinkedBlock = null;
            if (temp.LinkedBlock != null)
            {
                temp.Unlink();
            }
        }

        private CouplingBlock SearchForLinkable()
        {
            IMyCubeBlock block = (Entity as IMyCubeBlock);
            Vector3D start = block.WorldMatrix.Translation;
            Vector3D end = start + (block.WorldMatrix.Up * ConnectionRange);

            List<IHitInfo> hitlist = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(start, end, hitlist, 0);

            foreach (IHitInfo hit in hitlist)
            {
                if (!(hit.HitEntity is IMyCubeGrid)) continue;

                IMyCubeGrid cg = hit.HitEntity as IMyCubeGrid;

                IMySlimBlock slim = cg.GetCubeBlock(cg.WorldToGridInteger(hit.Position));

                if (slim != null && slim.FatBlock != null)
                {
                    CouplingBlock couple = slim.FatBlock.GameLogic.GetAs<CouplingBlock>();

                    if (couple != null && couple != this)
                    {
                        return couple;
                    }
                }
            }

            return null;
        }

        private bool ValidateLink(CouplingBlock linkable)
        {
            return LinkedBlock == null && this.Entity.WorldMatrix.Up.Dot(linkable.Entity.WorldMatrix.Up) < -0.5f;
        }

        private void DoKlang()
        {
            if (!((Entity as IMyCubeBlock).CubeGrid.Physics != null && LinkedBlock != null && (LinkedBlock.Entity as IMyCubeBlock).CubeGrid.Physics != null)) return;

            IMyCubeBlock blockA = (Entity as IMyCubeBlock);
            IMyCubeBlock blockB = (LinkedBlock.Entity as IMyCubeBlock);
            IMyCubeGrid gridA = blockA.CubeGrid;
            IMyCubeGrid gridB = blockB.CubeGrid;
            Matrix a = blockA.WorldMatrix;
            Matrix b = blockB.WorldMatrix;

            // dont apply physics if the block are on the same grid
            if (gridA == gridB) return;

            Vector3D comA = gridA.Physics.CenterOfMassWorld;
            Vector3D comB = gridB.Physics.CenterOfMassWorld;

            Vector3D offset = comA - comB;

            Vector4 color2 = Color.Gold;
            MySimpleObjectDraw.DrawLine(a.Translation, a.Translation + offset, MyStringId.GetOrCompute("cable"), ref color2, 0.05f, BlendTypeEnum.Standard);


            Vector3D com = gridA.Physics.CenterOfMassWorld - (offset / 2);
            Vector3D target = com - (a.Up * EngineOffset);
            MySimpleObjectDraw.DrawLine(com, target, MyStringId.GetOrCompute("cable"), ref color2, 0.05f, BlendTypeEnum.Standard);


            // apply positional forces
            ApplySpringForce(a.Forward, VirticalSpringForce);
            ApplySpringForce(a.Left, DepthSpringForce);
            ApplySpringForce(a.Up, HorizontalSpringForce, EngineOffset);

            ApplyTorque(Base6Directions.Direction.Left, 200000);
            ApplyTorque(Base6Directions.Direction.Forward, 200000);
            ApplyTorque(Base6Directions.Direction.Up, 200000);
        }

        private void ApplySpringForce(Vector3 direction, float force)
        {
            //IMPORTANT:
            // This function is being applied to both grids. 
            // the funciton run twice in the same physics frame. 
            // The linked block take turns being a or b

            IMyCubeBlock a = (Entity as IMyCubeBlock);
            IMyCubeBlock b = (LinkedBlock.Entity as IMyCubeBlock);

            Vector3D comA = a.CubeGrid.Physics.CenterOfMassWorld;
            Vector3D comB = b.CubeGrid.Physics.CenterOfMassWorld;
            Vector3D target = comA - comB;

            float dot = direction.Dot(target);
            Vector3 springForce = (direction * dot) * force;
            a.CubeGrid.Physics.AddForce(
                MyPhysicsForceType.APPLY_WORLD_FORCE,
                -springForce,
                comA,
                null
            );
        }

        private void ApplySpringForce(Vector3 direction, float force, float offset)
        {
            //IMPORTANT:
            // This function is being applied to both grids. 
            // the funciton run twice in the same physics frame. 
            // the linked blocks take turns being a or b

            IMyCubeBlock a = (Entity as IMyCubeBlock);
            IMyCubeBlock b = (LinkedBlock.Entity as IMyCubeBlock);

            Vector3D comA = a.CubeGrid.Physics.CenterOfMassWorld;
            Vector3D comB = b.CubeGrid.Physics.CenterOfMassWorld;

            Vector3D com = comA - ((comA - comB) / 2);
            Vector3D target = com - (direction * offset);

            float dot = direction.Dot(a.WorldMatrix.Translation - target);
            Vector3 springForce = (direction * dot) * HorizontalSpringForce;
            a.CubeGrid.Physics.AddForce(
                MyPhysicsForceType.APPLY_WORLD_FORCE,
                -springForce,
                comA,
                null
            );
        }

        private void ApplyTorque(Base6Directions.Direction direction, float force)
        {
            //IMPORTANT:
            // This function is being applied to both grids. 
            // the funciton runs twice in the same physics frame. 
            // the linked blocks take turns being a or b

            IMyCubeBlock blockA = (Entity as IMyCubeBlock);
            IMyCubeBlock blockB = (LinkedBlock.Entity as IMyCubeBlock);


            Matrix a = blockA.CubeGrid.WorldMatrix;
            Matrix b = blockB.CubeGrid.WorldMatrix;

            //Matrix b = blockB.CubeGrid.WorldMatrix * Matrix.Invert(RelativeRotation);

            Vector4 color = Color.Pink;
            Vector3 aDir = Vector3.Zero;
            Vector3 bDir = Vector3.Zero;
            switch (direction)
            {
                case Base6Directions.Direction.Up:
                    aDir = a.Up;
                    bDir = b.GetDirectionVector(up);
                    color = Color.Blue;
                    break;
                case Base6Directions.Direction.Left:
                    aDir = a.Left;
                    bDir = b.GetDirectionVector(left);
                    color = Color.Yellow;
                    break;
                case Base6Directions.Direction.Forward:
                    aDir = a.Forward;
                    bDir = b.GetDirectionVector(forward);
                    break;
            }

            Vector3 torque = Vector3.Cross(aDir, bDir);

            MySimpleObjectDraw.DrawLine(blockA.CubeGrid.Physics.CenterOfMassWorld, blockA.CubeGrid.Physics.CenterOfMassWorld + (torque * 30), MyStringId.GetOrCompute("cable"), ref color, 0.05f, BlendTypeEnum.Standard);

            Vector3 totalTorque = torque * force;
            blockA.CubeGrid.Physics.AddForce(
                MyPhysicsForceType.ADD_BODY_FORCE_AND_BODY_TORQUE,
                null,
                null,
                totalTorque);

        }
    }
}