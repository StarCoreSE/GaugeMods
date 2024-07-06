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
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Core : MySessionComponentBase
	{
		public const ushort ModId = 32144;
		public const string ModName = "GrappleHook";


        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
            Tools.DebugMode = false;
            NetworkAPI.LogNetworkTraffic = false;

			if (!NetworkAPI.IsInitialized)
			{
				NetworkAPI.Init(ModId, ModName);
			}
		}
    }
}

