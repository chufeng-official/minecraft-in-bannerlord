using System;
using System.IO;
using TaleWorlds.MountAndBlade;

namespace BannerlordBlocks
{
    public sealed class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Log.Write("Submodule loaded; prototype revision 9");
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            Log.Write("Mission initializing: " + mission.SceneName);
            foreach (MissionBehavior behavior in mission.MissionBehaviors)
                Log.Write("Behavior: " + behavior.GetType().FullName + "; assembly=" + behavior.GetType().Assembly.GetName().Name);
            // Never attach to campaign missions, multiplayer, or saved games.
            if (mission.MissionBehaviors.ExistsCustomBattle())
            {
                mission.AddMissionBehavior(new BlockMission());
                Log.Write("Attached to custom battle: " + mission.SceneName);
            }
            else Log.Write("Skipped: no CustomBattleAgentLogic");
        }
    }

    internal static class MissionFilter
    {
        public static bool ExistsCustomBattle(this System.Collections.Generic.List<MissionBehavior> behaviors)
        {
            return behaviors.Exists(b => b is CustomBattleAgentLogic);
        }
    }

    internal static class Log
    {
        internal static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordBlocks");
        internal static void Write(string text)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(Path.Combine(DirectoryPath, "prototype.log"),
                    DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
            }
            catch { /* Logging failure must not terminate the host mission. */ }
        }
    }
}
