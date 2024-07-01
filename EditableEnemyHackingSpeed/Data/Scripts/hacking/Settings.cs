using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.IO;
using VRage.Utils;

namespace BalancedHacking
{
    [ProtoContract]
    public class Settings
    {
        private const string Filename = "BalancedHackingSpeed.cfg";

        public static readonly Settings Default = new Settings
        {
            TerminalBlockHackSpeedAboveFunctional = 0.15f,
            TerminalBlockHackSpeedBelowFunctional = 0.2f,
            NonTerminalBlockHackSpeed = 0.04f,

            HandGunTerminalBlockHackSpeedAboveFunctional = 1f,
            HandGunTerminalBlockHackSpeedBelowFunctional = 1f,
            HandGunNonTerminalBlockHackSpeed = 0.5f
        };

        [ProtoMember(1)]
        public float TerminalBlockHackSpeedAboveFunctional { get; set; }
        [ProtoMember(2)]
        public float TerminalBlockHackSpeedBelowFunctional { get; set; }
        [ProtoMember(3)]
        public float NonTerminalBlockHackSpeed { get; set; }
        [ProtoMember(4)]
        public float HandGunTerminalBlockHackSpeedAboveFunctional { get; set; }
        [ProtoMember(5)]
        public float HandGunTerminalBlockHackSpeedBelowFunctional { get; set; }
        [ProtoMember(6)]
        public float HandGunNonTerminalBlockHackSpeed { get; set; }


        public static Settings Load()
        {
            Settings s = null;
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
                {
                    MyLog.Default.Info("[BalancedHackingSpeed] Loading saved settings");
                    TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
                    string text = reader.ReadToEnd();
                    reader.Close();

                    s = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);
                }
                else
                {
                    MyLog.Default.Info("[BalancedHackingSpeed] Config file not found. Loading default settings");
                    s = Default;
                    Save(s);
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Warning($"[BalancedHackingSpeed] Failed to load saved configuration. Loading defaults\n {e.ToString()}");
                s = Default;
                Save(s);
            }

            Save(s);
            return s;
        }

        public static void Save(Settings settings)
        {
            if (MyAPIGateway.Session.IsServer)
            {
                try
                {
                    MyLog.Default.Info("[BalancedHackingSpeed] Saving Settings");
                    TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(Filename, typeof(Settings));
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
                    writer.Close();
                }
                catch (Exception e)
                {
                    MyLog.Default.Error($"[BalancedHackingSpeed] Failed to save settings\n{e.ToString()}");
                }
            }
        }
    }
}
