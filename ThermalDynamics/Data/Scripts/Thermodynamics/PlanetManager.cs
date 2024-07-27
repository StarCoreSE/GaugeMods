using Draygo.BlockExtensionsAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Drawing;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PlanetManager : MySessionComponentBase
    {
        public static readonly double SunSize = 0.045f;
        public static readonly double Denominator = 1 - SunSize;
        public static readonly PlanetDefinition NullDef = new PlanetDefinition();

        public class Planet
        {
            public MyPlanet Entity;
            public Vector3D Position;
            public MyGravityProviderComponent GravityComponent;
            private PlanetDefinition definition = NullDef;
            public PlanetDefinition Definition() 
            {
                if (definition == NullDef && Entity.DefinitionId.HasValue) 
                {
                    definition = PlanetDefinition.GetDefinition(Entity.DefinitionId.Value);

                    MyLog.Default.Info($"[{Settings.Name}] updated planet definition: {Entity.DisplayName}");
                }

                return definition;
            }
        }

        public class ExternalForceData
        {
            public Vector3D Gravity = Vector3D.Zero;
            public Vector3D WindDirection = Vector3D.Zero;
            public float WindSpeed;
            public float AtmosphericPressure;
        }
 
        private static List<Planet> Planets = new List<Planet>();

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Entities.OnEntityAdd += AddPlanet;
            MyAPIGateway.Entities.OnEntityRemove += RemovePlanet;
        }

        private void AddPlanet(IMyEntity ent)
        {
            if (ent is MyPlanet)
            {
                MyPlanet entity = ent as MyPlanet;

                //MyLog.Default.Info($"[{Settings.Name}] Added Planet: {entity.DisplayName} - {entity.DefinitionId.HasValue}");
                Planets.Add(new Planet()
                {
                    Entity = entity,
                    Position = entity.PositionComp.WorldMatrixRef.Translation,
                    GravityComponent = entity.Components.Get<MyGravityProviderComponent>(),
                });
            }
        }

        private void RemovePlanet(IMyEntity ent)
        {
            Planets.RemoveAll(p => p.Entity.EntityId == ent.EntityId);
        }


        /// <summary>
        /// returns the gravity force vetor being applied at a location
        /// also returns total air pressure at that location
        /// </summary>
        public static ExternalForceData GetExternalForces(Vector3D worldPosition)
        {
            ExternalForceData data = new ExternalForceData();

            Planet planet = null;
            double distance = double.MaxValue;
            foreach (Planet p in Planets)
            {
                data.Gravity += p.GravityComponent.GetWorldGravity(worldPosition);

                double d = (p.Position - worldPosition).LengthSquared();
                if (d < distance)
                {
                    planet = p;
                    distance = d;
                }
            }

            if (planet?.Entity.HasAtmosphere == true)
            {
                data.AtmosphericPressure = planet.Entity.GetAirDensity(worldPosition);
                data.WindSpeed = planet.Entity.GetWindSpeed(worldPosition);
            }

            return data;
        }

        /// <summary>
        /// Finds the closest planet to the current position
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public static Planet GetClosestPlanet(Vector3D position) 
        {
            Planet current = null;
            double distance = double.MaxValue;
            for (int i = 0; i < Planets.Count; i++) 
            {
                Planet p = Planets[i];
                double d = (p.Position - position).LengthSquared();
                if (d < distance) 
                {
                    current = p;
                    distance = d;
                }
            }

            return current;
        }
    }
}
