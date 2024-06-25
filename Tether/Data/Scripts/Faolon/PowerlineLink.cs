using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace FaolonTether
{
    [ProtoContract]
    public class PowerlineLink
    {
        [ProtoMember(1)]
        public long PoleAId;

        [ProtoMember(2)]
        public string PoleAGridName;

        [ProtoMember(3)]
        public Vector3I PoleAPosition;

        [ProtoMember(4)]
        public long PoleBId;

        [ProtoMember(5)]
        public string PoleBGridName;

        [ProtoMember(6)]
        public Vector3I PoleBPosition;

        [XmlIgnore]
        public PowerlinePole PoleA;

        [XmlIgnore]
        public PowerlinePole PoleB;

        public static PowerlineLink Generate(PowerlinePole a, PowerlinePole b)
        {
            PowerlineLink link = new PowerlineLink();
            link.PoleA = a;
            link.PoleAId = a.Entity.EntityId;

            link.PoleB = b;
            link.PoleBId = a.Entity.EntityId;

            return link;
        }

        public void Bloat()
        {
            if (PoleA != null) return;

            IMyEntity a = MyAPIGateway.Entities.GetEntityById(PoleAId);
            IMyEntity b = MyAPIGateway.Entities.GetEntityById(PoleAId);

            if (a == null)
            {
                MyLog.Default.Info("[Tether] Error! could not find Powerline Pole: " + PoleAId);
                return;
            }

            if (b == null)
            {
                MyLog.Default.Info("[Tether] Error! could not find Powerline Pole: " + PoleBId);
                return;
            }

            PoleA = a.GameLogic.GetAs<PowerlinePole>();
            PoleB = b.GameLogic.GetAs<PowerlinePole>();

        }

        public void SavePrep()
        {
            if (PoleA == null) return;

            PoleAGridName = PoleA.Grid.DisplayName;
            PoleAPosition = PoleA.ModBlock.Position;

            if (PoleB == null) return;

            PoleBGridName = PoleB.Grid.DisplayName;
            PoleBPosition = PoleB.ModBlock.Position;

        }

        public void LoadPrep()
        {
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            IMyEntity gridA = null;
            IMyEntity gridB = null;
            MyAPIGateway.Entities.GetEntities(entities, e => (e.DisplayName == PoleAGridName ? (gridA = e) != e : false));
            MyAPIGateway.Entities.GetEntities(entities, e => (e.DisplayName == PoleBGridName ? (gridB = e) != e : false)) ;

            if (gridA != null)
            {
                IMySlimBlock block = ((MyCubeGrid)gridA).GetCubeBlock(PoleAPosition);

                MyLog.Default.Info($"[Tether] gridA cubeblock lookup: {block != null}");

                if (block != null && block.FatBlock != null)
                {
                    PoleA = block.FatBlock.GameLogic.GetAs<PowerlinePole>();

                    MyLog.Default.Info($"[Tether] gridA GameLogic lookup: {PoleA != null}");

                    if (PoleA != null)
                    {

                        MyLog.Default.Info($"[Tether] Updating id from: {PoleAId} to: {PoleA.Entity.EntityId}");
                        PoleAId = PoleA.Entity.EntityId;
                    }
                }
            }

            if (gridB != null)
            {
                IMySlimBlock block = ((MyCubeGrid)gridB).GetCubeBlock(PoleBPosition);

                MyLog.Default.Info($"[Tether] gridB cubeblock lookup: {block != null}");

                if (block != null && block.FatBlock != null)
                {
                    PoleB = block.FatBlock.GameLogic.GetAs<PowerlinePole>();

                    MyLog.Default.Info($"[Tether] gridB GameLogic lookup: {PoleB != null}");

                    if (PoleB != null)
                    {
                        MyLog.Default.Info($"[Tether] Updating id from: {PoleBId} to: {PoleB.Entity.EntityId}");
                        PoleBId = PoleB.Entity.EntityId;
                    }
                }
            }
        }

    }
}
