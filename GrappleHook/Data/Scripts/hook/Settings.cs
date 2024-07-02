using System;
using System.IO;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;

namespace BlinkDrive
{
	[ProtoContract]
	public class Settings
	{
		public static Settings Instance;

		public const string Filename = "GrappleHook.cfg";

        [ProtoMember(1)]
        public double Version;

        [ProtoMember(2)]
		public double RopeForce;

		[ProtoMember(3)]
		public float MaxRopeLength;

		[ProtoMember(4)]
		public float GrappleSpeed;


		public static void Init() 
		{
			if (Instance == null) 
			{
				Instance = Load();
			}
		}

		public static Settings GetDefaults()
		{
			return new Settings {
				Version = 1,
				RopeForce = 200000000d,
				MaxRopeLength = 300f,
                GrappleSpeed = 1.5f,
            };
		}

		public static Settings Load()
		{
			Settings defaults = GetDefaults();
			Settings settings = defaults;
			try
			{
				if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
				{
					MyLog.Default.Info("[GrappleHook] Loading saved settings");
					TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
					string text = reader.ReadToEnd();
					reader.Close();

					settings = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);

					if (settings.Version != defaults.Version)
					{
						MyLog.Default.Info($"[GrappleHook] Old version updating config {settings.Version}->{GetDefaults().Version}");
						settings = GetDefaults();
						Save(settings);
					}
				}
				else
				{
					MyLog.Default.Info("[GrappleHook] Config file not found. Loading default settings");
					Save(settings);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[GrappleHook] Failed to load saved configuration. Loading defaults\n {e.ToString()}");
				Save(settings);
			}

			return settings;
		}

		public static void Save(Settings settings)
		{
			try
			{
				MyLog.Default.Info($"[GrappleHook] Saving Settings");
				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(Filename, typeof(Settings));
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
				writer.Close();
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[GrappleHook] Failed to save settings\n{e.ToString()}");
			}
		}
	}
}
