using Draygo.API;
using Draygo.BlockExtensionsAPI;
using Sandbox.ModAPI;
using SENetworkAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace Thermodynamics
{
	[MySessionComponentDescriptor(MyUpdateOrder.Simulation)]
	public class Session : MySessionComponentBase
	{
        public const ushort ModID = 30323;
        public static DefinitionExtensionsAPI Definitions;

        public Session()
        {
            MyLog.Default.Info($"[{Settings.Name}] Setup Definition Extention API");
            Definitions = new DefinitionExtensionsAPI(Done);
        }

        private void Done()
        {
            MyLog.Default.Info($"[{Settings.Name}] Definition Extention API - Done");
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            NetworkAPI.Init(ModID, Settings.Name);
            NetworkAPI.LogNetworkTraffic = true;

            ThermalHud.Initialize();
        }

        protected override void UnloadData()
        {
            Definitions?.UnloadData();
            base.UnloadData();
        }

        public override void Simulate()
        {
            Debug.ShowDebugInfo();
        }

        public override void Draw()
        {
            ThermalHud.Draw();
        }
    }
}
