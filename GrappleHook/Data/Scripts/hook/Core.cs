using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ObjectBuilders;
using VRageMath;

namespace GrappleHook
{
	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class Core : MySessionComponentBase
	{

		private static List<WeaponControlLayer> hooks = new List<WeaponControlLayer>();

		public const ushort ModId = 32144;
		public const string ModName = "GrappleHook";
		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
            Tools.DebugMode = true;
            NetworkAPI.LogNetworkTraffic = true;

			if (!NetworkAPI.IsInitialized)
			{
				NetworkAPI.Init(ModId, ModName);
			}
		}

        public override void UpdateBeforeSimulation()
        {
			if (!MyAPIGateway.Utilities.IsDedicated) 
			{
                MyAPIGateway.Utilities.ShowNotification($"hooks: {hooks.Count}", 1);
            }

			try
			{
				lock (hooks)
				{
					for (int i = 0; i < hooks.Count; i++)
					{
						hooks[i].ApplyForce();
					}
				}
			}
			catch { }

        }

		public static void Add(WeaponControlLayer w) 
		{
            try
            {
                hooks.Add(w);
            }
            catch { }
        }

        public static void Remove(WeaponControlLayer w)
        {
			try
			{
				hooks.Remove(w);
			}
			catch { }
        }
    }
}

