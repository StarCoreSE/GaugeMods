using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public class Tools
    {

        public const float MWtoWatt = 1000000;
        public const float kWtoWatt = 1000;
        public const float KphToMps = 1000f / 60f / 60f;
        public const float BoltzmannConstant = 0.00000005670374419f;
        public const float VacuumTemperaturePower4 = 53.1441f; // vacuum temp is 2.7 kelven. 2.7^4 is 53.1441;
        public const float ConductivityScaler = 1f / 10000f;

        /// <summary>
        /// Converts a single axis direction vector into a number
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static int DirectionToIndex(Vector3I vector)
        {
            if (vector.X > 0) return 0;
            if (vector.X < 0) return 1;
            if (vector.Y > 0) return 2;
            if (vector.Y < 0) return 3;
            if (vector.Z > 0) return 4;
            return 5;
        }

        /// <summary>
        /// Converts the direction index into a vector
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public static Vector3 IndexToDirection(int index) 
        {
            switch (index) 
            {
                case 0:
                    return new Vector3(1, 0, 0);
                case 1:
                    return new Vector3(-1, 0, 0);
                case 2:
                    return new Vector3(0, 1, 0);
                case 3:
                    return new Vector3(0,-1, 0);
                case 4:
                    return new Vector3(0, 0, 1);
                case 5:
                    return new Vector3(0, 0, -1);
                default:
                    return Vector3.Zero;
            }
        }

        /// <summary>
        /// Generates a color based on the heat perameters
        /// </summary>
        /// <param name="temp">current temperature</param>
        /// <param name="max">maximum possible temprature</param>
        /// <param name="low">0 is black this value is blue</param>
        /// <param name="high">this value is red max value is white</param>
        /// <returns>HSV Vector3</returns>
        public static Vector3 GetTemperatureColor(float temp, float max = 2000, float low = 265f, float high = 500f)
        {
            // Clamp the temperature to the range 0-max
            float t = Math.Max(0, Math.Min(max, temp));

            float h = 240f / 360f;
            float s = 1;
            float v = 0.5f;

            if (t < low)
            {
                v = (1.5f * (t / low)) - 1;
            }
            else if (t < high)
            {
                h = (240f - ((t - low) / (high - low) * 240f)) / 360f;
            }
            else
            {
                h = 0;
                s = 1 - (2 * ((t - high) / (max - high)));
            }

            return new Vector3(h, s, v);
        }

        /// <summary>
        /// Calculates the surface area of the touching sides
        /// If you are using IMySlimBlock. add +1 to the max value
        /// </summary>
        public static int FindTouchingSurfaceArea(Vector3I minA, Vector3I maxA, Vector3I minB, Vector3I maxB)
        {
            // Check if they touch on the X face
            if (minA.X == maxB.X || maxA.X == minB.X)
            {
                int overlapY = Math.Min(maxA.Y, maxB.Y) - Math.Max(minA.Y, minB.Y);
                int overlapZ = Math.Min(maxA.Z, maxB.Z) - Math.Max(minA.Z, minB.Z);
                if (overlapY > 0 && overlapZ > 0)
                {
                    return overlapY * overlapZ;
                }
            }

            // Check if they touch on the Y face
            if (minA.Y == maxB.Y || maxA.Y == minB.Y)
            {
                int overlapX = Math.Min(maxA.X, maxB.X) - Math.Max(minA.X, minB.X);
                int overlapZ = Math.Min(maxA.Z, maxB.Z) - Math.Max(minA.Z, minB.Z);
                if (overlapX > 0 && overlapZ > 0)
                {
                    return overlapX * overlapZ;
                }
            }

            // Check if they touch on the Z face
            if (minA.Z == maxB.Z || maxA.Z == minB.Z)
            {
                int overlapX = Math.Min(maxA.X, maxB.X) - Math.Max(minA.X, minB.X);
                int overlapY = Math.Min(maxA.Y, maxB.Y) - Math.Max(minA.Y, minB.Y);
                if (overlapX > 0 && overlapY > 0)
                {
                    return overlapX * overlapY;
                }
            }

            return 0;
        }

        public static bool IsSolarOccluded(Vector3D observer, Vector3 solarDirection, MyPlanet planet)
        {
            Vector3D local = observer - planet.PositionComp.WorldMatrixRef.Translation;
            double distance = local.Length();
            Vector3D localNorm = local / distance;

            double dot = Vector3.Dot(localNorm, solarDirection);
            return dot < GetLargestOcclusionDotProduct(GetVisualSize(distance, planet.AverageRadius));
        }

        /// <summary>
        /// a number between 0 and 1 representing the side object based on distance
        /// </summary>
        /// <param name="distance">the distance between the observer and the target</param>
        /// <param name="radius">the size of the target</param>
        public static double GetVisualSize(double distance, double radius)
        {
            return 2 * Math.Atan(radius / (2 * distance));
        }

        /// <summary>
        /// an equation made by plotting the edge most angle of the occluded sun
        /// takes in the current visual size of the planet and produces a number between 0 and -1
        /// if the dot product of the planet and sun directions is less than this number it is occluded
        /// </summary>
        /// <param name="visualSize"></param>
        /// <returns></returns>
        public static double GetLargestOcclusionDotProduct(double visualSize)
        {
            return -1 + (0.85 * visualSize * visualSize * visualSize);
        }
    }
}
