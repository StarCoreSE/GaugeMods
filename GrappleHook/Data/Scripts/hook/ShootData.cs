using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace GrappleHook
{
    [ProtoContract]
    public class ShootData
    {
        [ProtoMember(1)]
        public Vector3D position;
        [ProtoMember(2)]
        public Vector3 direction;
    }
}
