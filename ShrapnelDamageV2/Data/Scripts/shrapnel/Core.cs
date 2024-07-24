using Sandbox.Definitions;
using Sandbox.ModAPI;
using System.Collections.Generic;
using System.IO;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Shrapnel
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Core : MySessionComponentBase
    {
        private Queue<ShrapnelData> queue = new Queue<ShrapnelData>();

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, ProcessDamage);
        }

        public void ProcessDamage(object target, ref MyDamageInformation info)
        {
            IMySlimBlock slim = target as IMySlimBlock;
            if (slim == null) return;

            float generalMult = 1f;
            if (slim.BlockDefinition is MyCubeBlockDefinition)
            {
                generalMult = ((MyCubeBlockDefinition)slim.BlockDefinition).GeneralDamageMultiplier;
            }

            MyLog.Default.Info($"generalMult:{generalMult}, amount1: {info.Amount}");

            float amount = generalMult * info.Amount;

            MyLog.Default.Info($"generalMult:{generalMult}, amount1: {info.Amount}, amount2: {amount}, integrity: {slim.Integrity}, overkill: {amount - slim.Integrity}");

            if (info.Type == MyDamageType.Weapon || info.Type == MyDamageType.Bullet || info.Type == MyDamageType.Rocket)
            {
                if (slim.Integrity >= amount) return;

                float overkill = amount - slim.Integrity;
                info.Amount = slim.Integrity;

                List<IMySlimBlock> n = new List<IMySlimBlock>();
                slim.GetNeighbours(n);

                queue.Enqueue(new ShrapnelData()
                {
                    Neighbours = n,
                    OverKill = overkill
                });
            }
            else if (info.Type == MyDamageType.Explosion && !(slim.FatBlock is IMyWarhead))
            {
                queue.Enqueue(new ShrapnelData()
                {
                    OverKill = amount,
                    Neighbours = new List<IMySlimBlock>() { slim },
                });
                info.Amount = 0;

            }

        }

        public override void UpdateBeforeSimulation()
        {
            int tasks = 0;
            while (queue.Count > 0 && tasks < 200)
            {
                tasks++;
                ShrapnelData data = queue.Dequeue();
                float count = 1f / (float)data.Neighbours.Count;

                MyLog.Default.Info($"queue: {queue.Count} - overkill: {data.OverKill}, spread: {data.OverKill * count}");
                foreach (IMySlimBlock neighbour in data.Neighbours)
                {
                    if (neighbour == null) continue;
                    //// get block resistance
                    //float generalMult = 1f;
                    //if (neighbour.BlockDefinition is MyCubeBlockDefinition)
                    //{
                    //    generalMult = ((MyCubeBlockDefinition)neighbour.BlockDefinition).GeneralDamageMultiplier;
                    //}

                    neighbour.DoDamage(data.OverKill * count, MyDamageType.Bullet, true);
                }
            }
        }
    }

    internal class ShrapnelData
    {
        public float OverKill { get; set; }
        public List<IMySlimBlock> Neighbours { get; set; }
    }
}
