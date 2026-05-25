using VRageMath;
using VRage.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using static VRageRender.MyBillboard;
using VRage.ObjectBuilders;
using Sandbox.ModAPI;
using System.Net;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.ComponentModel;
using System;


namespace PodracerCoupling
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "PodRacerCoupling")]
    public class CouplingBlock : MyGameLogicComponent
    {
        public int Range { get; } = 10;
        public CouplingBlock LinkedBlock { get; private set; }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void OnRemovedFromScene()
        {
            if(LinkedBlock != null)
            {
                LinkedBlock.Unlink();
            }
        }

        public override void UpdateBeforeSimulation()
        {
            Vector4 colorR = Color.Red;
            Vector4 colorG = Color.Green;
            Vector4 colorY = Color.Yellow;

            IMyCubeBlock block = (Entity as IMyCubeBlock);
            Vector3D start = block.WorldMatrix.Translation;
            Vector3D end = start + (block.WorldMatrix.Up * Range);


            if (LinkedBlock != null)
            {
                MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("cable"), ref colorG, 0.15f, BlendTypeEnum.Standard);
                return;
            }

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


        private void Link(CouplingBlock block)
        {
            if (ValidateLink(block))
            {
                LinkedBlock = block;
                block.LinkedBlock = this;
            }

        }

        public void Unlink()
        {
            if (LinkedBlock == null)
            {
                MyLog.Default.Warning("[CouplingBlock.Unlink] Unlink was called when no link existed. LinkedBlock was already null.");
                return;
            }

            if (LinkedBlock.LinkedBlock != null)
            {
                LinkedBlock.Unlink();
            }
            LinkedBlock = null;
        }


        private CouplingBlock SearchForLinkable()
        {
            IMyCubeBlock block = (Entity as IMyCubeBlock);
            Vector3D start = block.WorldMatrix.Translation;
            Vector3D end = start + (block.WorldMatrix.Up * Range);

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
    }
}