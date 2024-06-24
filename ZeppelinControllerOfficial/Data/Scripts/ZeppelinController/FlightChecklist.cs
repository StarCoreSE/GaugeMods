using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinCore
{
	public class FlightChecklist
	{
		public bool IsFlightReady => !IsDocked && HasCockpit && IsWorking;
		public bool IsDockedAndActive => IsDocked && HasCockpit && IsWorking;
		public bool HasAllComponents => HasCockpit && HasBallest && HasBalloon && HasExaust && HasGyro && HasFarm;

		public bool HasCockpit;
		public bool HasBalloon;
		public bool HasBallest;
		public bool HasExaust;
		public bool HasGyro;
		public bool HasFarm;
		public bool IsDocked;
		public bool IsWorking;
		public bool IsFunctional;
	}
}
