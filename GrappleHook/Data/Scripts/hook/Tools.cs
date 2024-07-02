using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace GrappleHook
{
	public class Tools
	{
		public const float Tick = 1f / 60f;
		public const float MillisecondPerFrame = 1000f / 60f;
		public const float FireRateMultiplayer = 1f / 60f / 60f;

		public const float MinutesToMilliseconds = 1f / 60f / 1000f;
		public const float TicksToMilliseconds = 1f / TimeSpan.TicksPerMillisecond;

		public static Random Random = new Random();

		public static T CastProhibit<T>(T ptr, object val) => (T)val;

		public static float MaxSpeedLimit => ((MyDefinitionManager.Static.EnvironmentDefinition.LargeShipMaxSpeed > MyDefinitionManager.Static.EnvironmentDefinition.SmallShipMaxSpeed) ?
			MyDefinitionManager.Static.EnvironmentDefinition.LargeShipMaxSpeed : MyDefinitionManager.Static.EnvironmentDefinition.SmallShipMaxSpeed) + 10;

		public static bool DebugMode = false;
		private const string Prefix = "[GrappleHook] ";

		public static void Info(string message)
		{
			MyLog.Default.Info($"{Prefix}{message}");
		}

		public static void Error(string message)
		{
			MyLog.Default.Error($"{Prefix}{message}");
		}

		public static void Warning(string message)
		{
			MyLog.Default.Warning($"{Prefix}{message}");
		}

		public static void Debug(string message)
		{
			if (DebugMode)
			{
				MyLog.Default.Info($"[DEBUG] {Prefix}{message}");
			}
		}

		public static float GetScalerInverse(float mult)
		{
			if (mult > 1)
			{
				mult = 1 / mult;
			}
			else
			{
				mult = 1 + (1 - mult);
			}
			return mult;
		}

		public static double AngleBetween(Vector3D norm1, Vector3D norm2)
		{
			float ratio = Vector3.Dot(norm1, norm2);

			double theta;

			if (ratio < 0)
			{
				theta = Math.PI - 2.0 * Math.Asin((-norm1 - norm2).Length() / 2.0);
			}
			else
			{
				theta = 2.0 * Math.Asin((norm1 - norm2).Length() / 2.0);
			}

			return theta * 180 / Math.PI;
		}

		private const int Seed = 5366354;
		private static float[] RandomSet;
		private static float[] RandomSetFromAngle;
		public static Vector3 ApplyDeviation(Vector3 direction, float maxAngle, ref sbyte index)
		{
			if (maxAngle == 0)
				return direction;

			if (RandomSet == null)
			{
				RandomSet = new float[128];
				RandomSetFromAngle = new float[128];

				Random rand = new Random(Seed);

				for (int i = 0; i < 128; i++)
				{
					RandomSet[i] = (float)(rand.NextDouble() * Math.PI * 2);
				}

				for (int i = 0; i < 128; i++)
				{
					RandomSetFromAngle[i] = (float)rand.NextDouble();
				}
			}

			if (index == 127)
			{
				index = 0;
			}
			else
			{
				index++;
			}

			Matrix matrix = Matrix.CreateFromDir(direction);

			float randomFloat = (RandomSetFromAngle[index] * maxAngle * 2) - maxAngle;
			float randomFloat2 = RandomSet[index];

			Vector3 normal = -new Vector3(MyMath.FastSin(randomFloat) * MyMath.FastCos(randomFloat2), MyMath.FastSin(randomFloat) * MyMath.FastSin(randomFloat2), MyMath.FastCos(randomFloat));
			return Vector3.TransformNormal(normal, matrix);
		}
		public static SerializableDefinitionId GetSelectedHotbarDefinition(IMyShipController cockpit)//, ref int index)
		{
			if (cockpit == null)
				return default(SerializableDefinitionId);

			MyObjectBuilder_ShipController builder = cockpit.GetObjectBuilderCubeBlock(false) as MyObjectBuilder_ShipController;

			int? slotIndex = builder?.Toolbar?.SelectedSlot;
			if (!slotIndex.HasValue)
				return default(SerializableDefinitionId);

			MyObjectBuilder_Toolbar toolbar = builder.Toolbar;
			if (toolbar.Slots.Count <= slotIndex.Value)
				return default(SerializableDefinitionId);

			var item = toolbar.Slots[slotIndex.Value];
			if (!(item.Data is MyObjectBuilder_ToolbarItemWeapon))
				return default(SerializableDefinitionId);

			//index = toolbar.SelectedSlot.Value;
			return (item.Data as MyObjectBuilder_ToolbarItemWeapon).defId;
		}
		public static float NormalizeAngle(int angle)
		{
			int num = angle % 360;
			if (num == 0 && angle != 0)
			{
				return 360f;
			}
			return num;
		}


		private static Dictionary<long, IMyGps> _gpsPoints = new Dictionary<long, IMyGps>();
		public static void AddGPS(long gridId, Vector3D target)
		{
			if (!_gpsPoints.ContainsKey(gridId))
			{
				_gpsPoints.Add(gridId, MyAPIGateway.Session.GPS.Create(gridId.ToString(), "", target, true));
				MyAPIGateway.Session.GPS.AddLocalGps(_gpsPoints[gridId]);
				MyVisualScriptLogicProvider.SetGPSColor(gridId.ToString(), Color.Orange);
				_gpsPoints[gridId].Name = "";
			}

			_gpsPoints[gridId].Coords = target;
		}

		public static void ClearGPS()
		{
			//if (!_wasInTurretLastFrame)
			//	return;

			foreach (IMyGps gps in _gpsPoints.Values)
			{
				MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
			}
			_gpsPoints.Clear();

			//_wasInTurretLastFrame = false;
		}

		public static void RemoveGPS(long id)
		{
			if (_gpsPoints.ContainsKey(id))
			{
				MyAPIGateway.Session.GPS.RemoveLocalGps(_gpsPoints[id]);
				_gpsPoints.Remove(id);
			}
		}

	}
}
