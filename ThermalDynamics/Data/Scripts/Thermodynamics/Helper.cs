﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using VRage.Noise.Combiners;
using VRageMath;

namespace Thermodynamics
{
    internal static class Helper
    {
        private const int size = 1024;
        private const int sizeSquared = size * size;

        /// <summary>
        /// flattens the 3D array into a linear representation
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static int Flatten(this Vector3I vector) 
        {
            return (sizeSquared * vector.Z) + (size * vector.Y) + vector.X;
        }

        /// <summary>
        /// replaces the contents of the current vector with the extractect x,y,z values
        /// </summary>
        /// <param name="flatVector"></param>
        /// <returns></returns>
        public static Vector3I Unflatten(this Vector3I vector, long flatVector)
        {
            vector.Z = (int)(flatVector / sizeSquared);
            flatVector %= sizeSquared;
            vector.Y = (int)(flatVector / size);
            vector.X = (int)(flatVector % size);

            return vector;
        }

        public static int LargestFace(this Vector3I vector)
        {
            int s1 = 1;
            int s2 = 1;
            for (int i = 0; i < 3; i++)
            {
                if (vector[i] >= s1) 
                {
                    s2 = s1;
                    s1 = vector[i];
                }
            }

            return s1*s2;
        }


    }
}
