
using Draygo.API;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SENetworkAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;


namespace ZeppelinCore
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "ZeppelinCore")]
	public class ZeppelinCoreBlock : MyGameLogicComponent
	{
		private const string BalloonName = "Cell";
		private const string BallastName = "Tank";
		private const string ExhaustName = "Vent";

		public const double UP_1 = (1f / 1000f);
		public const double DOWN_1 = (-1f / 1000f);
		public const double UP_10 = (10f / 1000f);
		public const double DOWN_10 = (-10f / 1000f);
		public const double UP_100 = (100f / 1000f);
		public const double DOWN_100 = (-100f / 1000f);

		public const double PIDTimeStep = 1f / 60f; // this needs to change if you want a less frequent update than once per frame

		private const double MaxTankRatio = 0.98;
		private const double MinTankRatio = 0.5;
		private const double FillErrorMargin = 0.0010;

		private const double MinimumAltitude = 0.001d;
		private const double MaximumAltitude = 15d;

		//private const double balloonForce = 755194.1960563; // k = 200 * 1000 / 200;
		private const double balloonForce = 1137782;//1154500;// k = 300 * 1000 / 200;
													//1121064
													//901650

		private static bool TerminalInitialized = false;

		private List<IMyGasTank> Balloons = new List<IMyGasTank>();
		private List<IMyGasTank> Ballasts = new List<IMyGasTank>();
		private List<IMyOxygenFarm> OxygenFarms = new List<IMyOxygenFarm>();
		private List<IMyThrust> Exhaust = new List<IMyThrust>();
		private List<IMyLandingGear> Gears = new List<IMyLandingGear>();
		private List<IMyShipConnector> Connectors = new List<IMyShipConnector>();
		private List<IMyGyro> Gyros = new List<IMyGyro>();
		private List<IMyShipController> Cockpits = new List<IMyShipController>();

		private bool IsRealGrid => ModBlock.CubeGrid.Physics != null;
		public double TargetAltitude => targetAltitude.Value;

		private PID AltitudeController = new PID(0.5, 1, 10);
		private PID PitchController = new PID(0.5, 0.1, 0.25);
		private PID RollController = new PID(0.5, 0.1, 0.25);

		private double feedBackThreshhold = 0.35;

		// update once before frame occurse before user actions and controls are setup.
		// this variable is used to wait one frame before setting up controls.
		private bool WaitFrameForControlSetup = true;

		private string loadedConfig = string.Empty;

		private NetSync<double> targetAltitude;

		public string Debug = "";

		public IMyCubeBlock ModBlock;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			ModBlock = Entity as IMyCubeBlock;

			targetAltitude = new NetSync<double>(this, TransferType.Both, 0, true, true);

			InitialGridScan();
			ModBlock.CubeGrid.OnBlockAdded += BlockAdded;
			ModBlock.CubeGrid.OnBlockRemoved += BlockRemoved;

			NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
			NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

			(ModBlock as IMyUpgradeModule).CustomDataChanged += OnCustomDataChanged;

		}

		public override void Close()
		{
			TurnOffZeppelinControl();
			ToggleGyroOnOff(false);


		}

		public bool IsActive()
		{
			return ModBlock.IsWorking;
		}

		/// <summary>
		/// Removes this zeppelin controller if there is another one present
		/// </summary>
		private void RemoveIfNotValid()
		{
			List<IMySlimBlock> slims = new List<IMySlimBlock>();
			ModBlock.CubeGrid.GetBlocks(slims, (s) => { return s.FatBlock != null && s.FatBlock.GameLogic.GetAs<ZeppelinCoreBlock>() != null; });

			if (slims.Count > 1)
			{
				if (!MyAPIGateway.Utilities.IsDedicated)
				{
					MyAPIGateway.Utilities.ShowNotification($"Only 1 Zeppelin Core allowed per grid", 3000, "Red");
				}

				ModBlock.CubeGrid.RemoveBlock(ModBlock.SlimBlock, true);
			}
		}

		/// <summary>
		/// Initializes the zeppelin controller and user interface
		/// </summary>
		public override void UpdateOnceBeforeFrame()
		{
			if (WaitFrameForControlSetup)
			{
				RemoveIfNotValid();
				ParseConfig();

				NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
				WaitFrameForControlSetup = false;
				return;
			}

			if (!TerminalInitialized)
			{
				MyLog.Default.Info($"[Zeppelin] Setting up controls and actions");
				if (!MyAPIGateway.Utilities.IsDedicated)
				{
					CreateTerminalControls();
				}

				CreateTerminalActions();

				TerminalInitialized = true;
			}
		}

		public override void UpdateBeforeSimulation()
		{
			if (!IsRealGrid)
				return;

			UpdateDisplay();
			ToggleGyroOnOff(IsActive());

			if (IsActive())
			{

				if (targetAltitude.Value == 0)
				{
					ResetTargetElevation();
				}

				RunUprightControl();

				// run server side only code
				if (!MyAPIGateway.Multiplayer.IsServer)
					return;

				FlightChecklist checklist = FlightCheck();

				if (checklist.IsFlightReady)
				{
					RunAltitudeControl();
				}
				else if (checklist.IsDocked)
				{
					RunDockedControl();
				}
			}
		}

		private void UpdateDisplay()
		{
		}

		/// <summary>
		/// scans the grid for all zeppelin components
		/// </summary>
		private void InitialGridScan()
		{
			List<IMySlimBlock> blocksList = new List<IMySlimBlock>();
			ModBlock.CubeGrid.GetBlocks(blocksList, b => b.FatBlock is IMyTerminalBlock);

			Balloons.Clear();
			Ballasts.Clear();
			OxygenFarms.Clear();
			Exhaust.Clear();
			Gears.Clear();
			Connectors.Clear();
			Gyros.Clear();

			foreach (IMySlimBlock slim in blocksList)
			{
				IMyCubeBlock fat = slim.FatBlock;
				if (!(fat is IMyTerminalBlock))
					continue;

				IMyTerminalBlock block = fat as IMyTerminalBlock;
				if (block == ModBlock)
					continue;

				if (block is IMyGasTank)
				{
					if (block.CustomName.Contains(BalloonName))
						Balloons.Add(block as IMyGasTank);
					else if (block.CustomName.Contains(BallastName))
						Ballasts.Add(block as IMyGasTank);

				}
				else if (block.BlockDefinition.SubtypeId == "LargeBlockHydrogenFarm")
				{
					OxygenFarms.Add(block as IMyOxygenFarm);
				}
				else if (block is IMyThrust)
				{
					if (block.CustomName.Contains(ExhaustName))
						Exhaust.Add(block as IMyThrust);
				}
				else if (block is IMyLandingGear)
				{
					Gears.Add(block as IMyLandingGear);
				}
				else if (block is IMyShipConnector)
				{
					Connectors.Add(block as IMyShipConnector);
				}
				else if (block is IMyShipController)
				{
					Cockpits.Add(block as IMyShipController);
				}
				else if (block is IMyGyro)
				{
					Gyros.Add(block as IMyGyro);
				}
			}
		}

		/// <summary>
		/// Registers the new block if it can be controlled buy the zeppelin core
		/// </summary>
		private void BlockAdded(IMySlimBlock slim)
		{
			IMyCubeBlock fat = slim.FatBlock;
			if (!(fat is IMyTerminalBlock))
				return;

			IMyTerminalBlock block = fat as IMyTerminalBlock;
			if (block == ModBlock)
				return;

			if (block is IMyGasTank)
			{
				if (block.CustomName.Contains(BalloonName))
					Balloons.Add(block as IMyGasTank);
				else if (block.CustomName.Contains(BallastName))
					Ballasts.Add(block as IMyGasTank);

			}
			else if (block.BlockDefinition.SubtypeId == "LargeBlockHydrogenFarm")
			{
				OxygenFarms.Add(block as IMyOxygenFarm);
			}
			else if (block is IMyThrust)
			{
				if (block.CustomName.Contains(ExhaustName))
					Exhaust.Add(block as IMyThrust);
			}
			else if (block is IMyLandingGear)
			{
				Gears.Add(block as IMyLandingGear);
			}
			else if (block is IMyShipConnector)
			{
				Connectors.Add(block as IMyShipConnector);
			}
			else if (block is IMyShipController)
			{
				Cockpits.Add(block as IMyShipController);
			}
			else if (block is IMyGyro)
			{
				Gyros.Add(block as IMyGyro);
			}
		}

		/// <summary>
		/// Unregisters the block being removed from the zeppelin core
		/// </summary>
		private void BlockRemoved(IMySlimBlock slim)
		{
			IMyCubeBlock fat = slim.FatBlock;
			if (!(fat is IMyTerminalBlock))
				return;

			IMyTerminalBlock block = fat as IMyTerminalBlock;
			if (block == ModBlock)
				return;

			if (block is IMyGasTank)
			{
				if (block.CustomName.Contains(BalloonName))
					Balloons.Remove(block as IMyGasTank);
				else if (block.CustomName.Contains(BallastName))
					Ballasts.Remove(block as IMyGasTank);

			}
			else if (block.BlockDefinition.SubtypeId == "LargeBlockHydrogenFarm")
			{
				OxygenFarms.Remove(block as IMyOxygenFarm);
			}
			else if (block is IMyThrust)
			{
				if (block.CustomName.Contains(ExhaustName))
					Exhaust.Remove(block as IMyThrust);
			}
			else if (block is IMyLandingGear)
			{
				Gears.Remove(block as IMyLandingGear);
			}
			else if (block is IMyShipConnector)
			{
				Connectors.Remove(block as IMyShipConnector);
			}
			else if (block is IMyShipController)
			{
				Cockpits.Remove(block as IMyShipController);
			}
			else if (block is IMyGyro)
			{
				Gyros.Remove(block as IMyGyro);
			}
		}

		public FlightChecklist FlightCheck()
		{
			FlightChecklist list = new FlightChecklist() {
				HasCockpit = Cockpits.Count != 0,
				HasBalloon = Balloons.Count != 0,
				HasBallest = Ballasts.Count != 0,
				HasExaust = Exhaust.Count != 0,
				HasGyro = Gyros.Count != 0,
				HasFarm = OxygenFarms.Count != 0,
				IsWorking = ModBlock.IsWorking,
				IsFunctional = ModBlock.IsFunctional,
			};

			if (!list.HasCockpit)
			{
				return list;
			}

			IMyShipController cockpit = GetCockpit();

			//get mass to verify docked status
			double physicalMass = cockpit.CalculateShipMass().PhysicalMass;
			double baseMass = cockpit.CalculateShipMass().BaseMass;

			// if physicalMass is less than baseMass, then it's likely the grid is locked to voxel or docked to station.
			if (ModBlock.CubeGrid.Physics.IsStatic || physicalMass < baseMass)
			{
				if (IsLandingGearLocked() || IsConnectorLocked())
				{
					list.IsDocked = true;
				}
			}

			return list;
		}

		public double FeedForward = 0;
		public double Feedback = 0;
		private void RunAltitudeControl()
		{
			IMyShipController cockpit = GetCockpit();

			if (cockpit == null)
				return;

			double physicalMass = (double)cockpit.CalculateShipMass().PhysicalMass;

			double currentAltitude = GetSealevelAltitude();
			double error = targetAltitude.Value - currentAltitude;
			double filledRatio = GetFilledRatio(Balloons);

			//run altitude controller
			//altitude controller here combines both feedforward and feedback control to determine the desired fill ratio
			//Feedforward control is very accurate but yields a pretty slow system response. 
			//Feedback control makes the settle time much faster by overfilling cells or underfilling to get faster acceleration. Feedback also allows for disturbance compensation.

			FeedForward = GetNeededFilledRatio(physicalMass, targetAltitude.Value);
			Feedback = AltitudeController.ControllerResponse(error, PIDTimeStep);

			Feedback = Math.Min(Math.Max(Feedback, -feedBackThreshhold), feedBackThreshhold);

			double controllerOutput = FeedForward + Feedback;

			AdjustFill(filledRatio, controllerOutput);
		}

		private void RunUprightControl()
		{
			IMyShipController cockpit = GetCockpit();

			if (cockpit == null)
				return;

			if (FlightCheck().IsDocked)
				return;

			Vector3D gravVec = -cockpit.GetNaturalGravity();

			Vector3D forward = cockpit.WorldMatrix.Forward;
			Vector3D right = cockpit.WorldMatrix.Right;
			Vector3D up = cockpit.WorldMatrix.Up;

			const double quarterCycle = Math.PI / 2;

			//PID control for pitch and roll
			//find the error for pitch and roll
			double pitchError = VectorAngleBetween(forward, gravVec) - quarterCycle;
			double rollError = VectorAngleBetween(right, gravVec) - quarterCycle;

			//run the PID control
			double pitchAccel = PitchController.ControllerResponse(pitchError, PIDTimeStep);
			double rollAccel = RollController.ControllerResponse(-rollError, PIDTimeStep);

			//apply angular acceelrations here
			Vector3D angularVel = cockpit.CubeGrid.Physics.AngularVelocity;
			angularVel += right * pitchAccel;
			angularVel += forward * rollAccel;

			cockpit.CubeGrid.Physics.AngularVelocity = angularVel;

			cockpit.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, null, cockpit.CubeGrid.Physics.CenterOfMassWorld, angularVel);
		}

		/// <summary>
		/// Manages the system when zeppelin is docked
		/// Resets flight controls and puts balloons/ballast vents and generators on standby
		/// Fills fuel to take off ready status
		/// </summary>
		private void RunDockedControl()
		{
			IMyShipController cockpit = GetCockpit();

			if (cockpit == null)
				return;

			double baseMass = (double)cockpit.CalculateShipMass().BaseMass;


			ResetTargetElevation();
			ResetPIDState();

			// when docked target altitude must be current altitude. this keeps the grid from rocketing up or down when releasing dock.
			double feedForward = GetNeededFilledRatio(baseMass, targetAltitude.Value);
			double filledRatio = GetFilledRatio(Balloons);

			AdjustFill(filledRatio, feedForward);
		}

		private void AdjustFill(double currentBalloonFillRatio, double targetFillRatio)
		{
			double ballestTankRatio = GetFilledRatio(Ballasts);

			// The deviation is used as a margin of error.
			// Attempting to be exact would cause constant fluctuation in height.
			double deviation = Math.Abs(targetFillRatio - currentBalloonFillRatio);

			//Apply the controller output.
			if (currentBalloonFillRatio < targetFillRatio && deviation > FillErrorMargin)
			{
				//increase ratio
				ToggleExhaust(false);
				ToggleGasStockpile(Balloons, true);
				ToggleGasStockpile(Ballasts, false);

			}
			else if (currentBalloonFillRatio > targetFillRatio && deviation > FillErrorMargin)
			{
				//decrease ratio
				ToggleExhaust(false);
				ToggleGasStockpile(Balloons, false);
				ToggleGasStockpile(Ballasts, true);

				//if the tanks are at capacity, start dumping hydrogen to sink.
				if (ballestTankRatio > MaxTankRatio)
				{
					ToggleGasStockpile(Ballasts, false);
					ToggleExhaust(true);
				}
				else
				{
					ToggleExhaust(false);
				}
			}
			else
			{
				//maintain ratio
				ToggleExhaust(false);
				ToggleGasStockpile(Balloons, false);
				ToggleGasStockpile(Ballasts, false);
			}

			//toggle the oxygen farms if the hydrogen tanks are not filled enough. 
			if (ballestTankRatio <= MinTankRatio)
			{
				ToggleOxygen(true);
			}
			else
			{
				ToggleOxygen(false);
			}
		}
		private double GetNeededFilledRatio(double shipMass, double desiredAltitude)
		{
			double ratio = (shipMass * 9.81f) / (Balloons.Count * balloonForce * GetAtmosphericDensity(desiredAltitude));
			return ratio;
		}

		private double GetAtmosphericDensity(double altitudeKM)
		{
			double eff = -0.0712151286 * altitudeKM + 0.999714809;
			if (eff > 1)
				eff = 1;

			if (eff < 0)
				eff = 0.01f;

			return eff;
		}

		private void ResetPIDState()
		{
			AltitudeController.Reset();
			PitchController.Reset();
			RollController.Reset();
		}

		public void ResetTargetElevation()
		{
			double sealevel = GetSealevelAltitude();
			if (targetAltitude.Value != sealevel)
			{
				targetAltitude.Value = sealevel;
			}
		}

		public double GetSealevelAltitude()
		{
			IMyShipController cockpit = GetCockpit();

			if (ModBlock == null || !IsRealGrid || cockpit == null)
				return 0.0;

			double altitude = 0;
			cockpit.TryGetPlanetElevation(Sandbox.ModAPI.Ingame.MyPlanetElevation.Sealevel, out altitude);
			return altitude / 1000;
		}

		public double GetSurfaceAltitude()
		{
			IMyShipController cockpit = GetCockpit();

			if (ModBlock == null || !IsRealGrid || cockpit == null)
				return 0.0;

			double surfaceAltitude = 0;
			cockpit.TryGetPlanetElevation(Sandbox.ModAPI.Ingame.MyPlanetElevation.Surface, out surfaceAltitude);
			return surfaceAltitude;
		}

		private IMyShipController GetCockpit()
		{
			if (HasCockpit())
			{
				return Cockpits[0];
			}
			return null;
		}

		private bool HasCockpit()
		{
			return Cockpits.Count != 0;
		}

		private bool IsLandingGearLocked()
		{
			for (int i = 0; i < Gears.Count; i++)
			{
				IMyLandingGear gear = Gears[i];
				if (gear.IsLocked)
				{
					return true;
				}
			}

			return false;
		}

		private bool IsConnectorLocked()
		{
			for (int i = 0; i < Connectors.Count; i++)
			{
				IMyShipConnector connector = Connectors[i];
				if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
				{
					return true;
				}
			}

			return false;
		}

		private void ToggleOxygen(bool on)
		{
			for (int i = 0; i < OxygenFarms.Count; i++)
			{
				IMyOxygenFarm farm = OxygenFarms[i];
				farm.Enabled = on;
			}
		}

		private void ToggleGasStockpile(List<IMyGasTank> tanks, bool on)
		{

			for (int i = 0; i < tanks.Count; i++)
			{
				IMyGasTank tank = tanks[i];
				tank.Stockpile = on;
				tank.Enabled = true;
			}
		}

		private void ToggleExhaust(bool on)
		{
			for (int i = 0; i < Exhaust.Count; i++)
			{
				IMyThrust thruster = Exhaust[i];
				thruster.Enabled = on;
				thruster.ThrustOverride = 100;
			}
		}

		private void ToggleGyroOnOff(bool onoff)
		{
			//This function turns all gyros on grid on or off.
			foreach (IMyGyro gyro in Gyros)
			{
				gyro.Enabled = onoff;
			}
		}

		public double CurrentBalloonFill()
		{
			return GetFilledRatio(Balloons);
		}

		public double TargetBalloonFill()
		{
			IMyShipController cockpit = GetCockpit();

			if (cockpit == null)
				return 0;

			double physicalMass = (double)cockpit.CalculateShipMass().PhysicalMass;
			double baseMass = (double)cockpit.CalculateShipMass().BaseMass;

			if (IsLandingGearLocked() || IsConnectorLocked())
			{
				return GetNeededFilledRatio(baseMass, targetAltitude.Value);
			}
			else
			{
				return GetNeededFilledRatio(physicalMass, targetAltitude.Value);
			}
		}

		public double BallastFill() 
		{
			return GetFilledRatio(Ballasts);
		}

		private double GetFilledRatio(List<IMyGasTank> tanks)
		{
			double total = 0;

			for (int i = 0; i < tanks.Count; i++)
			{
				IMyGasTank tank = tanks[i];
				total += tank.FilledRatio;
			}

			total /= tanks.Count;
			return total;
		}

		private double VectorAngleBetween(Vector3D a, Vector3D b)
		{ //returns radians
		  //Law of cosines to return the angle between two vectors.

			if (a.LengthSquared() == 0 || b.LengthSquared() == 0)
				return 0;
			else
				return Math.Acos(MathHelper.Clamp(a.Dot(b) / a.Length() / b.Length(), -1, 1));
		}

		public double GetVerticalVelocity()
		{
			IMyShipController cockpit = GetCockpit();

			if (cockpit != null)
			{
				Vector3D gravVec = cockpit.GetNaturalGravity();
				gravVec.Normalize();
				return -cockpit.GetShipVelocities().LinearVelocity.Dot(gravVec);
			}

			return 0;
		}

		private void TurnOffZeppelinControl()
		{
			//this function is called when a zeppelin controller is disabled.
			//This sets all ballasts and all balloons to neutral position
			foreach (IMyGasTank balloon in Balloons)
			{
				balloon.Stockpile = false;
				balloon.Enabled = true;
			}

			foreach (IMyGasTank ballast in Ballasts)
			{
				ballast.Stockpile = false;
				ballast.Enabled = true;
			}
		}

		public void ChangeTargetAltitude(double amountKM)
		{
			double value = targetAltitude.Value + amountKM;
			if (value < MinimumAltitude)
			{
				targetAltitude.SetValue(MinimumAltitude);
			}
			else if (value > MaximumAltitude)
			{
				targetAltitude.SetValue(MaximumAltitude);
			}
			else
			{
				targetAltitude.SetValue(targetAltitude.Value + amountKM);
			}
		}

		public void PushAltitudeChange()
		{
			targetAltitude.Push();
		}

		private void WriteConfigPID()
		{
			StringBuilder text = new StringBuilder();
			text.Append($"### kP is the speed that you accelerate towards your desired altitude\n");
			text.Append($"### kI is the integral dont mess with this unless you know what you are doing!\n");
			text.Append($"### kD increase this to avoid overshoot.\n\n");

			text.Append($"Altitude kP = {AltitudeController.kP}\n");
			text.Append($"Altitude kI = {AltitudeController.kI}\n");
			text.Append($"Altitude kD = {AltitudeController.kD}\n");

			(ModBlock as IMyUpgradeModule).CustomData = text.ToString();

		}

		private void ParseConfig()
		{
			string text = (ModBlock as IMyUpgradeModule).CustomData;
			if (loadedConfig == text)
				return;

			string[] lines = text.Split('\n');

			foreach (string line in lines)
			{
				if (!line.Contains("="))
					continue;

				string[] split = line.Split('=');
				split[0].Trim();
				split[1].Trim();

				PID control = null;
				if (split[0].Contains("Altitude"))
					control = AltitudeController;

				if (control == null)
					continue;

				if (split[0].Contains("kP"))
					control.kP = Convert.ToDouble(split[1]);
				if (split[0].Contains("kI"))
					control.kI = Convert.ToDouble(split[1]);
				if (split[0].Contains("kD"))
					control.kD = Convert.ToDouble(split[1]);
			}

			loadedConfig = text;
		}

		private void OnCustomDataChanged(IMyTerminalBlock b)
		{
			ParseConfig();
		}

		#region Terminal Controls

		private const string ID_ZepAltitude = "Zeppelin Altitude";
		private const string ID_ZepPID_Label = "Zeppelin PID Label";
		private const string ID_ZepPID_P = "Zeppelin PID P";
		private const string ID_ZepPID_I = "Zeppelin PID I";
		private const string ID_ZepPID_D = "Zeppelin PID D";
		private const string ID_ZepPID_Save = "Zeppelin PID Save";

		private const string ID_ZepClimb1 = "Zeppelin Climb 1m";
		private const string ID_ZepClimb10 = "Zeppelin Climb 10m";
		private const string ID_ZepClimb100 = "Zeppelin Climb 100m";
		private const string ID_ZepDrop1 = "Zeppelin Drop 1m";
		private const string ID_ZepDrop10 = "Zeppelin Drop 10m";
		private const string ID_ZepDrop100 = "Zeppelin Drop 100m";

		private void CreateTerminalControls()
		{
			IMyTerminalControlLabel label;
			IMyTerminalControlSlider slider;
			IMyTerminalControlButton button;


			//***********************************************
			//			Zeppelin Altitude Slider
			//***********************************************

			slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>(ID_ZepAltitude);
			slider.Visible = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			slider.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };

			slider.Setter = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.targetAltitude.Value = value;
			};

			slider.Getter = (block) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return 0;

				return (float)zep.targetAltitude.Value;
			};

			slider.Writer = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep != null)
				{
					value.Append($"{zep.targetAltitude.Value.ToString("n3")} km");
				}
			};

			slider.Title = MyStringId.GetOrCompute("Target Altitude");
			slider.Tooltip = MyStringId.GetOrCompute("km Distance above sea level");
			slider.SetLimits((float)MinimumAltitude, (float)MaximumAltitude);
			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(slider);


			//***********************************************
			//			Zeppelin Altitude PID Label
			//***********************************************

			label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyUpgradeModule>(ID_ZepPID_Label);
			label.Visible = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			label.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			label.Label = MyStringId.GetOrCompute("PID");

			//***********************************************
			//			Zeppelin Altitude PID P
			//***********************************************

			slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>(ID_ZepPID_P);
			slider.Visible = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			slider.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };

			slider.Setter = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.AltitudeController.kP = value;
			};

			slider.Getter = (block) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return 0;

				return (float)zep.AltitudeController.kP;
			};

			slider.Writer = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep != null)
				{
					value.Append($"{zep.AltitudeController.kP.ToString("n3")}");
				}
			};

			slider.Title = MyStringId.GetOrCompute("kP");
			slider.Tooltip = MyStringId.GetOrCompute("How hard you accelerate towards the target");
			slider.SetLimits(0f, 100f);
			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(slider);

			//***********************************************
			//			Zeppelin Altitude PID I
			//***********************************************

			slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>(ID_ZepPID_I);
			slider.Visible = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			slider.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };

			slider.Setter = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.AltitudeController.kI = value;
			};

			slider.Getter = (block) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return 0;

				return (float)zep.AltitudeController.kI;
			};

			slider.Writer = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep != null)
				{
					value.Append($"{zep.AltitudeController.kI.ToString("n3")}");
				}
			};

			slider.Title = MyStringId.GetOrCompute("kI");
			slider.Tooltip = MyStringId.GetOrCompute("Integral - dont touch this!");
			slider.SetLimits(0f, 10f);
			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(slider);

			//***********************************************
			//			Zeppelin Altitude PID D
			//***********************************************

			slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>(ID_ZepPID_D);
			slider.Visible = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			slider.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };

			slider.Setter = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.AltitudeController.kD = value;
			};

			slider.Getter = (block) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return 0;

				return (float)zep.AltitudeController.kD;
			};

			slider.Writer = (block, value) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep != null)
				{
					value.Append($"{zep.AltitudeController.kD.ToString("n3")}");
				}
			};

			slider.Title = MyStringId.GetOrCompute("kD");
			slider.Tooltip = MyStringId.GetOrCompute("Increase this for faster deceleration to avoid overshoot.");
			slider.SetLimits(0f, 100f);
			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(slider);

			//***********************************************
			//			Zeppelin PID Save button
			//***********************************************

			button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyUpgradeModule>(ID_ZepPID_Save);
			button.Visible = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			button.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };

			button.Action = (block) => {
				ZeppelinCoreBlock zep = block.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.WriteConfigPID();
			};

			button.Title = MyStringId.GetOrCompute("Save PID");
			MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(button);

		}

		private void CreateTerminalActions()
		{
			IMyTerminalAction action;

			//***********************************************
			//				Zeppelin Climb 1
			//***********************************************

			action = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>(ID_ZepClimb1);
			action.Name.Append(ID_ZepClimb1);
			action.Icon = "Textures\\GUI\\Icons\\Actions\\Increase.dds";
			action.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			action.Writer = (b, str) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				str.Append($"+1m");
			};

			action.Action = (b) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.targetAltitude.Value += UP_1;
			};

			MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(action);

			//***********************************************
			//				Zeppelin Climb 10
			//***********************************************

			action = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>(ID_ZepClimb10);
			action.Name.Append(ID_ZepClimb10);
			action.Icon = "Textures\\GUI\\Icons\\Actions\\Increase.dds";
			action.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			action.Writer = (b, str) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				str.Append($"+10m");
			};

			action.Action = (b) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.targetAltitude.Value += UP_10;
			};

			MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(action);

			//***********************************************
			//				Zeppelin Climb 100
			//***********************************************

			action = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>(ID_ZepClimb100);
			action.Name.Append(ID_ZepClimb100);
			action.Icon = "Textures\\GUI\\Icons\\Actions\\Increase.dds";
			action.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			action.Writer = (b, str) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				str.Append($"+100m");
			};

			action.Action = (b) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.targetAltitude.Value += UP_100;
			};

			MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(action);

			//***********************************************
			//				Zeppelin Drop 1
			//***********************************************

			action = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>(ID_ZepDrop1);
			action.Name.Append(ID_ZepDrop1);
			action.Icon = "Textures\\GUI\\Icons\\Actions\\Decrease.dds";
			action.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			action.Writer = (b, str) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				str.Append($"-1m");
			};

			action.Action = (b) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.targetAltitude.Value += DOWN_1;
			};

			MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(action);

			//***********************************************
			//				Zeppelin Drop 10
			//***********************************************

			action = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>(ID_ZepDrop10);
			action.Name.Append(ID_ZepDrop10);
			action.Icon = "Textures\\GUI\\Icons\\Actions\\Decrease.dds";
			action.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			action.Writer = (b, str) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				str.Append($"-10m");
			};

			action.Action = (b) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.targetAltitude.Value += DOWN_10;
			};

			MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(action);

			//***********************************************
			//				Zeppelin Drop 100
			//***********************************************

			action = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>(ID_ZepDrop100);
			action.Name.Append(ID_ZepDrop100);
			action.Icon = "Textures\\GUI\\Icons\\Actions\\Decrease.dds";
			action.Enabled = (b) => { return b.GameLogic.GetAs<ZeppelinCoreBlock>() != null; };
			action.Writer = (b, str) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				str.Append($"-100m");
			};

			action.Action = (b) => {
				ZeppelinCoreBlock zep = b.GameLogic.GetAs<ZeppelinCoreBlock>();

				if (zep == null)
					return;

				zep.targetAltitude.Value += DOWN_100;
			};

			MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(action);
		}

		#endregion
	}
}
