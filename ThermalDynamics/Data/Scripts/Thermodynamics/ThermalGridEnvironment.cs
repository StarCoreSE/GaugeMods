using Sandbox.Game;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRageMath;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using System;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {


        public Vector3 FrameSolarDirection;

        public float FrameAmbientTemprature;
        public float FrameAmbientTempratureP4;
        public bool FrameSolarOccluded;
        public float FrameAmbientDensity;
        public float FrameAmbientConvectionCoefficient;
        public float FrameAirDensityCurve;
        public float FrameEffectiveWindSpeed;
        public Vector3 FrameEffectiveWindDirection;
        public float FrameEffectiveConvectionCoefficient;
        public float FrameEffectiveSolarEnergy;

        private void PrepareNextSimulationStep()
        {
            CriticalBlocks = CurrentCriticalBlocks;
            CurrentCriticalBlocks = 0;

            //GridHeatGeneration = CurrentGridHeatGeneration;
            //CurrentGridHeatGeneration = 0;

            Vector3D position = Grid.PositionComp.WorldAABB.Center;
            PrepareSolarEnvironment(ref position);
            PrepareEnvironmentTemprature(ref position);
        }

        private void PrepareEnvironmentTemprature(ref Vector3D position)
        {

            if (!Settings.Instance.EnableEnvironment) return;

            if (!Settings.Instance.EnablePlanets)
            {
                SetFrameAmbiantTemperature(Settings.Instance.VacuumTemperature);
                return;
            }

            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(position);
            if (planet == null)
            {
                SetFrameAmbiantTemperature(Settings.Instance.VacuumTemperature);
                return;
            }

            PlanetDefinition def = planet.Definition();
            Vector3 local = position - planet.Position;
            Vector3D surfacePointLocal = planet.Entity.GetClosestSurfacePointLocal(ref local);
            bool isUnderground = planet.Entity.IsUnderGround(position); //local.LengthSquared() < surfacePointLocal.LengthSquared();
            FrameAmbientDensity = planet.Entity.GetAirDensity(position);

            // scales off less linearly
            float invert = (1 - FrameAmbientDensity);
            float invert2 = invert * invert;
            FrameAirDensityCurve = 1 - (invert2 * invert2);

            FrameAmbientConvectionCoefficient = def.ConvectionCoefficient;

            // Calculate effective wind including grid velocity
            if (FrameAmbientDensity > 0 && Grid.Physics != null)
            {
                float windSpeed = MyVisualScriptLogicProvider.GetWeatherIntensity(position);
                if (windSpeed == 0)
                {
                    windSpeed = planet.Entity.GetWindSpeed(position);    
                }

                Vector3 windDirection = Vector3.Cross(planet.GravityComponent.GetWorldGravityNormalized(position), planet.Entity.WorldMatrix.Forward).Normalized();
                Vector3 windVector = windDirection * windSpeed;
                Vector3 gridVelocity = Grid.Physics.LinearVelocity;
                Vector3 relativeVelocity = windVector - gridVelocity;
                FrameEffectiveWindSpeed = relativeVelocity.Length();
                FrameEffectiveWindDirection = FrameEffectiveWindSpeed > 0 ? Vector3.Normalize(relativeVelocity) : Vector3.Zero;
                FrameEffectiveConvectionCoefficient = FrameAmbientConvectionCoefficient * (1.0f + 0.1f * (float)Math.Pow(FrameEffectiveWindSpeed, 0.5f));
            }
            else
            {
                FrameEffectiveWindSpeed = 0;
                FrameEffectiveWindDirection = Vector3.Zero;
                FrameEffectiveConvectionCoefficient = FrameAmbientConvectionCoefficient;
            }

            float ambient = def.UndergroundTemperature;
            if (!isUnderground)
            {
                float dot = (float)Vector3D.Dot(Vector3D.Normalize(local), FrameSolarDirection);
                ambient = def.NightTemperature + ((dot + 1f) * 0.5f * (def.DayTemperature - def.NightTemperature));
            }
            else
            {
                FrameSolarOccluded = true;
            }

            SetFrameAmbiantTemperature(Math.Max(Settings.Instance.VacuumTemperature, ambient * FrameAirDensityCurve));

            // Calculate effective solar energy with atmospheric decay
            FrameEffectiveSolarEnergy = Settings.Instance.SolarEnergy * (1f - def.SolarDecay * FrameAirDensityCurve);

            //TODO: implement underground core temparatures
        }

        private void SetFrameAmbiantTemperature(float temperature)
        {
            FrameAmbientTemprature = temperature;
            float frameAmbiSquared = FrameAmbientTemprature * FrameAmbientTemprature;
            FrameAmbientTempratureP4 = frameAmbiSquared * frameAmbiSquared;
        }

        private void PrepareSolarEnvironment(ref Vector3D position)
        {
            if (!Settings.Instance.EnableSolarHeat) return;

            FrameSolarDirection = MyVisualScriptLogicProvider.GetSunDirection();

            FrameSolarOccluded = false;
            FrameMatrix = Grid.WorldMatrix;

            LineD line = new LineD(position, position + (FrameSolarDirection * 15000000));
            _overlapResultPool.Clear();
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, _overlapResultPool);
            LineD subLine;

            for (int i = 0; i < _overlapResultPool.Count; i++)
            {
                MyLineSegmentOverlapResult<MyEntity> ent = _overlapResultPool[i];
                MyEntity e = ent.Element;

                if (e is MyPlanet)
                {
                    MyPlanet myPlanet = e as MyPlanet;
                    Vector3D planetLocal = position - myPlanet.PositionComp.WorldMatrixRef.Translation;
                    double distance = planetLocal.Length();
                    Vector3D planetDirection = planetLocal / distance;

                    double dot = Vector3D.Dot(planetDirection, FrameSolarDirection);
                    double occlusionDot = Tools.GetLargestOcclusionDotProduct(Tools.GetVisualSize(distance, myPlanet.AverageRadius));

                    if (dot < occlusionDot)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }

                if (e is MyVoxelBase)
                {
                    MyVoxelBase voxel = e as MyVoxelBase;
                    if (voxel.RootVoxel is MyPlanet) continue;

                    voxel.PositionComp.WorldAABB.Intersect(ref line, out subLine);
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);

                    if (Settings.Instance.DebugSolarRaycast && !MyAPIGateway.Utilities.IsDedicated)
                    {
                        var green = Color.Green.ToVector4();
                        MySimpleObjectDraw.DrawLine(subLine.From, subLine.To, MyStringId.GetOrCompute("Square"), ref green, 0.2f);
                    }

                    IHitInfo hit;
                    MyAPIGateway.Physics.CastRay(subLine.From, subLine.To, out hit, 28); // 28

                    if (hit != null)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }

                if (e is MyCubeGrid && e.Physics != null && e.EntityId != Grid.EntityId)
                {
                    MyCubeGrid g = (e as MyCubeGrid);
                    _gridPool.Clear();
                    g.GetConnectedGrids(GridLinkTypeEnum.Physical, _gridPool);

                    for (int j = 0; j < _gridPool.Count; j++)
                    {
                        if (_gridPool[j].EntityId == Grid.EntityId) continue;
                    }

                    g.PositionComp.WorldAABB.Intersect(ref line, out subLine);

                    var blue = Color.Blue.ToVector4();

                    if (Settings.Instance.DebugSolarRaycast && !MyAPIGateway.Utilities.IsDedicated)
                    {

                        MySimpleObjectDraw.DrawLine(subLine.From, subLine.To, MyStringId.GetOrCompute("Square"), ref blue, 0.2f);
                    }

                    Vector3I? hit = (e as MyCubeGrid).RayCastBlocks(subLine.From, subLine.To);

                    if (hit.HasValue)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }
            }

            if (Settings.Instance.DebugSolarRaycast && !MyAPIGateway.Utilities.IsDedicated)
            {
                var color = (FrameSolarOccluded) ? Color.Red.ToVector4() : Color.White.ToVector4();
                MySimpleObjectDraw.DrawLine(position, position + (FrameSolarDirection * 15000000), MyStringId.GetOrCompute("Square"), ref color, 0.1f);
            }

            if (Settings.Instance.DebugWindRaycast && !MyAPIGateway.Utilities.IsDedicated)
            {
                var color = Color.Green.ToVector4();
                MySimpleObjectDraw.DrawLine(position, position - (FrameEffectiveWindDirection*FrameEffectiveWindSpeed), MyStringId.GetOrCompute("Square"), ref color, 0.1f);
            }
        }
    }
}
