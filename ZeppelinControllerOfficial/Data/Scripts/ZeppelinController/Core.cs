using Draygo.API;
using Sandbox.Game;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Input;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;


namespace ZeppelinCore
{
	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class Core : MySessionComponentBase
	{
		public const ushort ModID = 30024;
		public const string ModName = "Zeppelin Core";

		public static HudAPIv2 hudBase;
		public static HudAPIv2.HUDMessage hudStatus;
		public static HudAPIv2.HUDMessage hudOperationalReadout;

		private static StringBuilder hudStatusText = new StringBuilder("");
		private static StringBuilder hudOperationalReadoutText = new StringBuilder("");

		private float hudTextScale = 0.9f;

		ZeppelinCoreBlock ZepCore = null;

		private bool Initialized = false;

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			NetworkAPI.Init(ModID, ModName);
			NetworkAPI.LogNetworkTraffic = true;

			hudBase = new HudAPIv2(hudInit);
		}

		private void playerControlChanged(IMyControllableEntity o, IMyControllableEntity n)
		{
			ZepCore = null;

			if (n?.Entity is IMyShipController)
			{
				List<IMySlimBlock> temp = new List<IMySlimBlock>();
				(n.Entity as IMyTerminalBlock).CubeGrid.GetBlocks(temp, (b) => {
					if (ZepCore != null || b.FatBlock == null)
						return false;

					ZeppelinCoreBlock zb = b.FatBlock.GameLogic.GetAs<ZeppelinCoreBlock>();
					if (zb != null)
					{
						ZepCore = zb;
					}

					return false;
				});
			}
		}

		public override void UpdateBeforeSimulation()
		{
			if (!Initialized)
			{
				if (MyAPIGateway.Utilities.IsDedicated)
				{
					Initialized = true;
				}
				else if (MyAPIGateway.Session?.LocalHumanPlayer?.Controller != null)
				{
					MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntityChanged += playerControlChanged;
					playerControlChanged(null, MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity);
					Initialized = true;
				}
			}

			if (ZepCore == null)
			{
				if (hudBase.Heartbeat)
				{
					hudStatus.Visible = false;
					hudOperationalReadout.Visible = false;
				}

				return;
			}

			HandleKeyPress();

			if (!hudBase.Heartbeat)
				return;

			hudStatus.Visible = true;
			hudOperationalReadout.Visible = true;

			FlightChecklist checklist = ZepCore.FlightCheck();

			hudStatusText.Clear();
			hudOperationalReadoutText.Clear();

			if (ZepCore.IsActive())
			{
				if (checklist.IsFlightReady)
				{
					hudStatusText.Append($"Zeppelin: Active");
					hudStatus.InitialColor = Color.LightGreen;
				}
				else if (checklist.IsDockedAndActive)
				{
					hudStatusText.Append($"Zeppelin: Docked");
					hudStatus.InitialColor = Color.Orange;
				}
				else
				{
					hudStatusText.Append($"Zeppelin: Inactive");
					hudStatus.InitialColor = Color.Red;
				}
			}
			else
			{
				hudStatusText.Append($"Zeppelin: Inactive");
				hudStatus.InitialColor = Color.Red;
			}

			ZeppelinErrorStatus(hudStatusText, checklist);


			double surfaceAltitude = ZepCore.GetSurfaceAltitude();
			double currentAltitude = ZepCore.GetSealevelAltitude();

			hudOperationalReadoutText.Append($"--- Altitude ---\n");
			hudOperationalReadoutText.Append($"Target: {ZepCore.TargetAltitude.ToString("n3")}km\n");
			hudOperationalReadoutText.Append($"Current: {currentAltitude.ToString("n3")}km\n");
			hudOperationalReadoutText.Append($"Surface:{surfaceAltitude.ToString("n0")}m\n");
			hudOperationalReadoutText.Append($"Vert Speed: {Math.Round(ZepCore.GetVerticalVelocity(), 2)}m/s\n\n");
			hudOperationalReadoutText.Append($"-- Gas Levels ---\n");
			hudOperationalReadoutText.Append($"Target: {(ZepCore.TargetBalloonFill() * 100).ToString("n2")}%\n");
			hudOperationalReadoutText.Append($"Current: {(ZepCore.CurrentBalloonFill() * 100).ToString("n2")}%\n");
			hudOperationalReadoutText.Append($"Ballast: {(ZepCore.BallastFill() * 100).ToString("n2")}%\n\n");

			if (MyAPIGateway.Session.IsServer)
			{
				hudOperationalReadoutText.Append($"-- PID --\n");
				hudOperationalReadoutText.Append($"FeedForward: {ZepCore.FeedForward.ToString("n3")}\n");
				hudOperationalReadoutText.Append($"FeedBack: {ZepCore.Feedback.ToString("n3")}\n");
			}

			if (!string.IsNullOrEmpty(ZepCore.Debug))
			{
				hudOperationalReadoutText.Append($"Debug: {ZepCore.Debug}\n");
			}
		}


		private double value = 0.0001d;
		private int ticks = 0;
		private bool lastKeyPressed = false;
		private void HandleKeyPress()
		{
			if (MyAPIGateway.Gui.IsCursorVisible)
				return;

			List<MyKeys> keys = new List<MyKeys>();
			MyAPIGateway.Input.GetPressedKeys(keys);

			bool iskeyPressed = false;
			if (ZepCore.IsActive())
			{
				foreach (MyKeys key in keys)
				{
					if (key == MyKeys.Space)
					{
						ZepCore.ChangeTargetAltitude(value);
						iskeyPressed = true;
					}
					else if (key == MyKeys.C)
					{
						ZepCore.ChangeTargetAltitude(-value);
						iskeyPressed = true;
					}
				}
			}

			if (iskeyPressed)
			{
				ticks++;

				if (ticks % 180 == 0)
				{
					ZepCore.PushAltitudeChange();
					value += 0.001d;
				}
			}
			else
			{
				if (lastKeyPressed != iskeyPressed)
				{
					ZepCore.PushAltitudeChange();
				}

				value = 0.0001d;
				ticks = 0;
			}

			lastKeyPressed = iskeyPressed;
		}

		private void ZeppelinErrorStatus(StringBuilder text, FlightChecklist list)
		{
			if (!ZepCore.IsActive())
			{
				if (!ZepCore.ModBlock.IsFunctional)
				{
					text.Append("\nCORE DAMGED");
				}
				else
				{
					text.Append("\nCORE TOGGLED OFF");
				}
			}

			if (!list.HasAllComponents)
			{

				text.Append("\nMissing Systems");

				if (!list.HasBalloon)
				{
					text.Append("\nBalloon");
				}

				if (!list.HasBallest)
				{
					text.Append("\nHydrogen Tank");
				}

				if (!list.HasExaust)
				{
					text.Append("\nHydrogen Vent (not required)");
				}

				if (!list.HasGyro)
				{
					text.Append("\nGyro (not required)");
				}

				if (!list.HasFarm)
				{
					text.Append("\nHydrogen Farm (not required)");
				}

			}
		}

		private void hudInit()
		{
			hudStatus = new HudAPIv2.HUDMessage(hudStatusText, new Vector2D(-1, 0), null, -1, 1, true, false, null, BlendTypeEnum.PostPP, "white");
			hudStatus.InitialColor = Color.Green;
			hudStatus.Scale *= hudTextScale;
			hudStatus.Origin = new Vector2D(-1, 1);
			hudStatus.Visible = true;

			hudOperationalReadout = new HudAPIv2.HUDMessage(hudOperationalReadoutText, new Vector2D(-1, 0), null, -1, 1, true, false, null, BlendTypeEnum.PostPP, "white");
			hudOperationalReadout.InitialColor = Color.AntiqueWhite;
			hudOperationalReadout.Scale *= hudTextScale;
			hudOperationalReadout.Origin = new Vector2D(-1, 0.8f);
			hudOperationalReadout.Visible = true;
		}
	}
}
