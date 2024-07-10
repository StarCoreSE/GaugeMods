using Sandbox.Game.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace Thermodynamics
{
    public class ThermalRadiationNode
    {
        public float[] Sides = new float[6];
        public float[] SideAverages = new float[6];
        public int[] SideSurfaces = new int[6];

        /// <summary>
        /// Run at the end of the heat tick to setup for the next
        /// </summary>
        public void Update() 
        {
            for (int i = 0; i < 6; i++)
            {
                SideAverages[i] = (SideSurfaces[i] > 0) ? Sides[i] / (float)SideSurfaces[i] : 0;
            }

            Array.Clear(Sides, 0, Sides.Length);
            Array.Clear(SideSurfaces, 0, SideSurfaces.Length);
        }
    }
}
