using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace GrappleHook
{
	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class Core : MySessionComponentBase
	{
		public const ushort ModId = 32144;
		public const string ModName = "GrappleHook";

        public static double raycastDistance = 0;


        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
            Tools.DebugMode = true;
            NetworkAPI.LogNetworkTraffic = true;

			if (!NetworkAPI.IsInitialized)
			{
				NetworkAPI.Init(ModId, ModName);
			}
		}

        //public override void UpdateBeforeSimulation()
        //{
        //    if (MyAPIGateway.Utilities.IsDedicated) return;
        //    if (MyAPIGateway.Session.Player?.Character == null) return;

        //    IMyCamera cam = MyAPIGateway.Session.Camera;
        //    if (cam == null) return;

        //    Vector3D position = cam.WorldMatrix.Translation;
        //    Vector3D distance = cam.WorldMatrix.Forward * 2.5f;

        //    Vector3D endPosition = position + distance;

        //    IHitInfo hit = null;
        //    MyAPIGateway.Physics.CastRay(cam.WorldMatrix.Translation, position + distance, out hit);
        //    if (hit != null)
        //    {
        //        endPosition = hit.Position;
        //        raycastDistance = 7.5f * hit.Fraction;
        //    }
        //    else 
        //    {
        //        raycastDistance = 7.5f;
        //    }
        //}
    }
}

