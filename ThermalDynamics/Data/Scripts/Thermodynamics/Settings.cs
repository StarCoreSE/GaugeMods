using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
	[ProtoContract]
	public class Settings
	{
		public const string Filename = "ThermodynamicsConfig.cfg";
		public const string Name = "Thermodynamics";


		public const bool DebugTextureColors = true;

		public static Settings Instance;

        public static readonly MyStringHash DefaultSubtypeId = MyStringHash.GetOrCompute("DefaultThermodynamics");
        public static readonly MyStringHash DefaultLoopSubtypeId = MyStringHash.GetOrCompute("DefaultThermodynamicsLoop");

        [ProtoMember(1)]
		public int Version;

		[ProtoMember(2)]
        public bool DebugTextOnScreen;

		[ProtoMember(3)]
        public bool DebugTemperatureBlockColors;

        [ProtoMember(4)]
        public bool DebugSolarRadiationBlockColors;

		[ProtoMember(5)]
		public bool DebugSolarRaycast;

        [ProtoMember(8)]
		public bool EnableEnvironment;

		[ProtoMember(9)]
		public bool EnableSolarHeat;

		[ProtoMember(10)]
		public bool EnablePlanets;


		[ProtoMember(14)]
		public bool EnableDamage;

        /// <summary>
        /// the number of update cycles per second
        /// </summary>
        [ProtoMember(15)]
		public int Frequency;

		/// <summary>
		/// the desired sim speed
		/// this will increase the frequency without changing the TimeScale
		/// </summary>
		[ProtoMember(16)]
		public float SimulationSpeed;

		/// <summary>
		/// the temperature in kelven for space
		/// </summary>
		[ProtoMember(30)]
		public float VacuumTemperature;

		/// <summary>
		/// SolarEnergy = watts/m^2
		/// </summary>
		[ProtoMember(40)]
		public float SolarEnergy;


        /// <summary>
        /// Used to adjust values that are calculated in seconds, to the current time scale 
        /// </summary>
        [XmlIgnore]
		public float TimeScaleRatio;

		[XmlIgnore]
		public float PerSecond;

		public static Settings GetDefaults()
		{
			Settings s = new Settings {
				Version = 1,
				DebugTextOnScreen = true,
				DebugTemperatureBlockColors = true,
				DebugSolarRadiationBlockColors = false,
				DebugSolarRaycast = false,
				EnableEnvironment = true,
				EnableSolarHeat = true,
				EnablePlanets = true,
				EnableDamage = true,
				Frequency = 1,
				SimulationSpeed = 1,
				VacuumTemperature = 2.7f,
                SolarEnergy = 1000f,
            };

			s.Init();
			return s;	
		}

		private void Init() {

			if (Frequency < 1)
				Frequency = 1;

			TimeScaleRatio =  1f/Frequency;
			PerSecond = Frequency * SimulationSpeed;
		}

		public static Settings Load()
		{
			Settings defaults = GetDefaults();
			Settings settings = defaults;
			try
			{
				if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
				{
					MyLog.Default.Info($"[{Name}] Loading saved settings");
					TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
					string text = reader.ReadToEnd();
					reader.Close();

					settings = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);

					if (settings.Version != defaults.Version)
					{
						MyLog.Default.Info($"[{Name}] Old version updating config {settings.Version}->{GetDefaults().Version}");
						settings = GetDefaults();
						Save(settings);
					}
				}
				else
				{
					MyLog.Default.Info($"[{Name}] Config file not found. Loading default settings");
					Save(settings);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[{Name}] Failed to load saved configuration. Loading defaults\n {e.ToString()}");
				Save(settings);
			}

			settings.Init();
			return settings;
		}

		public static void Save(Settings settings)
		{
			try
			{
				MyLog.Default.Info($"[{Name}] Saving Settings");
				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(Filename, typeof(Settings));
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
				writer.Close();
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[{Name}] Failed to save settings\n{e.ToString()}");
			}
		}
	}
}
