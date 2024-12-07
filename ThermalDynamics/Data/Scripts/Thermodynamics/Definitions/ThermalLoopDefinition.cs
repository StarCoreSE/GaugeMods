using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Draygo.BlockExtensionsAPI;
using ProtoBuf;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    [ProtoContract]
    public class ThermalLoopDefintion
    {
        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalLoopProperties");
        private static readonly MyStringId MassId = MyStringId.GetOrCompute("Mass");
        private static readonly MyStringId ConductivityId = MyStringId.GetOrCompute("Conductivity");
        private static readonly MyStringId SpecificHeatId = MyStringId.GetOrCompute("SpecificHeat");
        private static readonly MyStringId PipeSurfaceAreaScalerId = MyStringId.GetOrCompute("PipeSurfaceAreaScaler");
        private static readonly MyStringId PlateSurfaceAreaScalerId = MyStringId.GetOrCompute("PlateSurfaceAreaScaler");

        public static readonly MyDefinitionId DefaultLoopDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultLoopSubtypeId);

        [ProtoMember(1)]
        public float Mass;

        /// <summary>
        /// Conductivity equation: watt / ( meter * Temp)
        /// For examples see https://www.engineeringtoolbox.com/thermal-conductivity-metals-d_858.html
        /// </summary>
        [ProtoMember(5)]
        public float Conductivity;

        /// <summary>
        /// SpecificHeat equation: watt / (mass_kg * temp_kelven)
        /// For examples see https://en.wikipedia.org/wiki/Table_of_specific_heat_capacities
        /// </summary>
        [ProtoMember(10)]
        public float SpecificHeat;

        /// <summary>
        /// The surface area scaler for the pipe segments
        /// </summary>
        [ProtoMember(15)]
        public float PipeSurfaceAreaScaler;

        /// <summary>
        /// The surface area scaler for the plate connection to other blocks
        /// </summary>
        [ProtoMember(20)]
        public float PlateSurfaceAreaScaler;

        public static ThermalLoopDefintion GetDefinition(MyDefinitionId defId)
        {
            ThermalLoopDefintion def = new ThermalLoopDefintion();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            double dvalue;
            if (!lookup.DefinitionIdExists(defId) || !lookup.TryGetDouble(defId, GroupId, PipeSurfaceAreaScalerId, out dvalue))
            {
                defId = new MyDefinitionId(defId.TypeId, Settings.DefaultSubtypeId);

                if (!lookup.DefinitionIdExists(defId))
                {
                    defId = DefaultLoopDefinitionId;
                }
            }

            if (lookup.TryGetDouble(defId, GroupId, MassId, out dvalue))
                def.Mass = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConductivityId, out dvalue))
                def.Conductivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SpecificHeatId, out dvalue))
                def.SpecificHeat = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, PipeSurfaceAreaScalerId, out dvalue))
                def.PipeSurfaceAreaScaler = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, PlateSurfaceAreaScalerId, out dvalue))
                def.PlateSurfaceAreaScaler = (float)dvalue;


            def.Mass = Math.Max(1, def.Mass);

            def.Conductivity = Math.Min(1, Math.Max(0, def.Conductivity));

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.PipeSurfaceAreaScaler = Math.Max(0, def.PipeSurfaceAreaScaler);

            def.PlateSurfaceAreaScaler = Math.Max(0, def.PlateSurfaceAreaScaler);

            return def;
        }
    }
}

