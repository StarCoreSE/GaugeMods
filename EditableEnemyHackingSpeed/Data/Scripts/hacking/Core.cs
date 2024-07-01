using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace BalancedHacking
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class Core : MySessionComponentBase
    {
        private Settings config = null;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            config = Settings.Load();
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, Handler);
        }

        private void Handler(object target, ref MyDamageInformation info)
        {
            IMySlimBlock block = target as IMySlimBlock;
            if (block == null) return; // End: Only reduce damage to blocks

            IMyEntity ent = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
            if (ent == null) return; // End: Must be a player character

            IMyHandheldGunObject<MyToolBase> tool = null;
            IMyHandheldGunObject<MyGunBase> gun = null;

            if (info.Type == MyDamageType.Grind)
            {
                tool = ent as IMyAngleGrinder;
            }
            else if (info.Type == MyDamageType.Drill)
            {
                tool = ent as IMyHandDrill;
            }
            else
            {
                gun = ent as IMyAutomaticRifleGun;
            }

            if (tool == null && gun == null) return; // End: damage must be done by tool or hand weapon

            bool isTool = tool != null;
            long identity = (isTool) ? tool.OwnerIdentityId : gun.OwnerIdentityId;

            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => p.IdentityId == identity);
            if (players.Count == 0 || // End: Player not found. this can happen if the character is an NPC
                block.CubeGrid.BigOwners.Count == 0) return;  // End: Unowned structures take full damage

            MyRelationsBetweenPlayerAndBlock relation = players[0].GetRelationTo(block.CubeGrid.BigOwners[0]);
            if (!(relation == MyRelationsBetweenPlayerAndBlock.Enemies || 
                relation == MyRelationsBetweenPlayerAndBlock.Neutral)) return; // End: Friendly and unowned structures take full damage

            if (block.FatBlock != null && block.FatBlock is IMyTerminalBlock)
            {
                if (block.FatBlock.OwnerId != 0)
                {
                    info.Amount = info.Amount * (1 + (1 - MyAPIGateway.Session.HackSpeedMultiplier)) * GetTerminalBlockHackSpeedAboveFunctional(isTool);
                }
                else
                {
                    info.Amount = info.Amount * GetTerminalBlockHackSpeedBelowFunctional(isTool);
                }

            }
            else
            {
                info.Amount = info.Amount * GetNonTerminalBlockHackSpeed(isTool);
            }
        }

        private float GetTerminalBlockHackSpeedAboveFunctional(bool isTool)
        {
            return (isTool) ? config.TerminalBlockHackSpeedAboveFunctional : config.HandGunTerminalBlockHackSpeedAboveFunctional;
        }

        private float GetTerminalBlockHackSpeedBelowFunctional(bool isTool)
        {
            return (isTool) ? config.TerminalBlockHackSpeedBelowFunctional : config.HandGunTerminalBlockHackSpeedBelowFunctional;
        }

        private float GetNonTerminalBlockHackSpeed(bool isTool)
        {
            return (isTool) ? config.NonTerminalBlockHackSpeed : config.HandGunNonTerminalBlockHackSpeed;
        }

    }
}
