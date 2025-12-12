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
        private static readonly Guid StorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619343");
        private static readonly Guid StorageGuidLoops = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619344");

        private string Pack()
        {
            byte[] bytes = new byte[Thermals.Count * 6];

            int bi = 0;
            for (int i = 0; i < Thermals.Count; i++)
            {
                ThermalCell c = Thermals.Cells[i];
                if (c == null) continue;

                int id = c.Id;
                bytes[bi] = (byte)id;
                bytes[bi + 1] = (byte)(id >> 8);
                bytes[bi + 2] = (byte)(id >> 16);
                bytes[bi + 3] = (byte)(id >> 24);

                short t = (short)(c.Temperature);
                bytes[bi + 4] = (byte)t;
                bytes[bi + 5] = (byte)(t >> 8);

                bi += 6;

                //TODO keep track of this. it might cause weird behaivor.
                c.Temperature = t;
            }

            return Convert.ToBase64String(bytes);
        }

        private void Unpack(string data)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(data);

                for (int i = 0; i < bytes.Length; i += 6)
                {
                    int id = bytes[i];
                    id |= bytes[i + 1] << 8;
                    id |= bytes[i + 2] << 16;
                    id |= bytes[i + 3] << 24;

                    int f = bytes[i + 4];
                    f |= bytes[i + 5] << 8;

                    //MyLog.Default.Info($"[{Settings.Name}] [Unpack] {id} {PositionToIndex[id]} {Thermals.ItemArray[PositionToIndex[id]].Block.BlockDefinition.Id} - T: {f}");

                    ThermalCell cell = Thermals.GetByPosition(id);
                    if (cell != null)
                    {
                        cell.Temperature = f;
                    }
                }


            }
            catch { }
        }

        private string PackLoops()
        {
            byte[] bytes = new byte[ThermalLoops.Count * 3];

            int bi = 0;
            for (int i = 0; i < ThermalLoops.Count; i++)
            {
                ThermalLoop l = ThermalLoops[i];
                if (l == null) continue;

                bytes[bi] = (byte)i;
                short t = (short)(l.Temperature);
                bytes[bi + 1] = (byte)t;
                bytes[bi + 2] = (byte)(t >> 8);

                bi += 3;

                //TODO: keep track of this. it might cause weird behaivor.
                l.Temperature = t;
            }

            return Convert.ToBase64String(bytes);
        }

        private void UnpackLoops(string data)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(data);

                for (int i = 0; i < bytes.Length; i += 3)
                {
                    int id = bytes[i];

                    int f = bytes[i+1];
                    f |= bytes[i+2] << 8;

                    //MyLog.Default.Info($"[{Settings.Name}] [UnpackLoops] {id} - T: {f}");

                    ThermalLoops[id].Temperature = f;
                }
            }
            catch { }
        }

        private void Save()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            string data = Pack();
            string loopdata = PackLoops();


            MyModStorageComponentBase storage = Entity.Storage;
            if (storage.ContainsKey(StorageGuid))
            {
                storage[StorageGuid] = data;
            }
            else
            {
                storage.Add(StorageGuid, data);
            }

            if (storage.ContainsKey(StorageGuidLoops))
            {
                storage[StorageGuidLoops] = loopdata;
            }
            else
            {
                storage.Add(StorageGuidLoops, loopdata);
            }

            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [SAVE] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms, size: {data.Length}");
        }

        private void Load()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            if (Entity.Storage.ContainsKey(StorageGuid))
            {
                Unpack(Entity.Storage[StorageGuid]);
            }

            if (Entity.Storage.ContainsKey(StorageGuidLoops))
            {
                UnpackLoops(Entity.Storage[StorageGuidLoops]);
            }

            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [LOAD] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");
        }
    }
}
