using System;
using VRageMath;

namespace Thermodynamics
{
    public class ThermalLoop
    {
        internal ThermalCell[] Loop;

        public float Temperature;

        private float C;
        private float kAPlate;
        private float kAPipe;

        private ThermalGrid Grid;
        private ThermalLoopDefintion Definition;

        public ThermalLoop(ThermalGrid g, ThermalLoopDefintion def, ThermalCell[] loop)
        {
            Grid = g;
            Loop = loop;
            Definition = def;

            PrecalculateVariables();
        }

        internal void PrecalculateVariables() 
        {
            float area = (Grid.Grid.GridSize * Grid.Grid.GridSize);
            float cpart = (Definition.SpecificHeat * Definition.Mass * Grid.Grid.GridSize);
            C = 1 / cpart * Settings.Instance.TimeScaleRatio;

            kAPlate = Definition.Conductivity * (cpart / (area * Definition.PlateSurfaceAreaScaler * Loop.Length) ) * area * Definition.PlateSurfaceAreaScaler;
            kAPipe = Definition.Conductivity * (cpart / (area * Definition.PipeSurfaceAreaScaler * Loop.Length) ) * area * Definition.PipeSurfaceAreaScaler;
        }

        internal void Update()
        {
            for (int i = 0; i < Loop.Length; i++)
            {
                // transfer heat to the pipes
                ThermalCell ncell = Loop[i];
                Temperature = Math.Max(0, Temperature + (C * kAPipe * (ncell.Temperature - Temperature)));
                ncell.Temperature = Math.Max(0, ncell.Temperature + (ncell.C * kAPipe * (Temperature - ncell.Temperature)));


                // transfer heat to the blocks connected via plate surface
                Matrix m;
                ncell.Block.Orientation.GetMatrix(out m);
                Vector3I[] surfaces = ThermalGrid.CoolantPlateDirections[ncell.Block.BlockDefinition.Id.SubtypeId.ToString()];
                for (int k = 0; k < surfaces.Length; k++)
                {
                    Vector3I dir;
                    Vector3I.Transform(ref surfaces[k], ref m, out dir);
                    Vector3I n = ncell.Block.Position + dir;
                    
                    for (int j = 0; j < ncell.Neighbors.Count; j++)
                    {
                        ThermalCell neighbor = ncell.Neighbors[j];

                        Vector3I min = neighbor.Block.Min;
                        Vector3I max = neighbor.Block.Max;

                        if (n.X >= min.X && n.Y >= min.Y && n.Z >= min.Z &&
                            n.X < max.X && n.Y < max.Y && n.Z < max.Z)
                        {
                            Temperature = Math.Max(0, Temperature + (C * kAPlate * (neighbor.Temperature - Temperature)));
                            neighbor.Temperature = Math.Max(0, neighbor.Temperature + (neighbor.C * kAPlate * (Temperature - neighbor.Temperature)));
                            break;
                        }
                    }
                }
            }
        }
    }
}
