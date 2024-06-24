using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
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
        [ProtoMember(3)]
        public long PoleBId;

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
    
    }
}
