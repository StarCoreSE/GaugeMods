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
    public class AttachData
    {
        [ProtoMember(1)]
        public long entityId;
        [ProtoMember(2)]
        public Vector3 localAttachmentPoint;
        [ProtoMember(3)]
        public Vector3I localAttachmentPointI;
    }
}
