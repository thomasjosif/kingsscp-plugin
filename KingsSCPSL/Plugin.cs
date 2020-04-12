/*
* +==================================================================================+
* |  _  ___                   ____  _                                             _  |
* | | |/  (_) _ __ __ _ ___  |  _ \| | __ _ _   _  __ _ _ __ ___  _   _ _ __   __| | |
* | | ' /| | '_ \ / _` / __| | |_) | |/ _` | | | |/ _` | '__/ _ \| | | | '_ \ / _` | |
* | | . \| | | | | (_| \__ \ |  __/| | (_| | |_| | (_| | | | (_) | |_| | | | | (_| | |
* | |_|\_\_|_| |_|\__, |___/ |_|   |_|\__,_|\__, |\__, |_|  \___/ \__,_|_| |_|\__,_| |
* |               |___/                     |___/ |___/                              |
* |                                                                                  |
* +==================================================================================+
* | SCPSL All-In-One Plugin                                                          |
* | Custom configurations for SCP:SL                                                 |
* | by Thomasjosif                                                                   |
* |                                                                                  |
* | https://kingsplayground.fun                                                      |
* +==================================================================================+
* | MIT License                                                                      |
* |                                                                                  |
* | Copyright (C) 2020 Thomas Dick                                                   |
* | Copyright (C) 2020 King's Playground                                             |
* |                                                                                  |
* | Permission is hereby granted, free of charge, to any person obtaining a copy     |
* | of this software and associated documentation files (the "Software"), to deal    |
* | in the Software without restriction, including without limitation the rights     |
* | to use, copy, modify, merge, publish, distribute, sublicense, and/or sell        |
* | copies of the Software, and to permit persons to whom the Software is            |
* | furnished to do so, subject to the following conditions:                         |
* |                                                                                  |
* | The above copyright notice and this permission notice shall be included in all   |
* | copies or substantial portions of the Software.                                  |
* |                                                                                  |
* | THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR       |
* | IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,         |
* | FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE      |
* | AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER           |
* | LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,    |
* | OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE    |
* | SOFTWARE.                                                                        |
* +==================================================================================+
*/

using EXILED;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MEC;
using Scp914;
using Harmony;


namespace KingsSCPSL
{
	public class Plugin : EXILED.Plugin
	{
		//Instance variable for eventhandlers
		public EventHandlers EventHandlers;

		public CoroutineHandle cor;
        public CustomInventory Inventories = new CustomInventory();
        public static List<CoroutineHandle> Coroutines = new List<CoroutineHandle>();
		public Dictionary<RoleType, int> roleHealth = new Dictionary<RoleType, int>();
        public static string APIKey;

        public override void OnEnable()
		{
            Dictionary<string, string> configHealth = KConf.ExiledConfiguration.GetDictonaryValue(Config.GetString("util_role_health", "NtfCommander:400,NtfScientist:350"));

            try
            {
                foreach (KeyValuePair<string, string> kvp in configHealth)
                {
                    roleHealth.Add((RoleType)Enum.Parse(typeof(RoleType), kvp.Key), int.Parse(kvp.Value));
                }
                Log.Info("Loaded " + configHealth.Keys.Count() + "('s) default health classes.");
            }
            catch (Exception e)
            {
                Log.Error("Failed to add custom health to roles. Check your 'util_role_health' config values for errors!\n" + e);
            }
            try
            {
                Inventories = new CustomInventory();

                Inventories.ClassD = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(
                        Config.GetString("util_classd_inventory", null)));
                Inventories.Chaos = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(Config.GetString("util_chaos_inventory",
                        null)));
                Inventories.NtfCadet = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(Config.GetString("util_ntfcadet_inventory",
                        null)));
                Inventories.NtfCommander = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(Config.GetString("util_ntfcommander_inventory",
                        null)));
                Inventories.NtfLieutenant = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(
                        Config.GetString("util_ntflieutenant_inventory", null)));
                Inventories.NtfScientist = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(Config.GetString("util_ntfscientist_inventory",
                        null)));
                Inventories.Scientist = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(Config.GetString("util_scientist_inventory",
                        null)));
                Inventories.Guard = CustomInventory.ConvertToItemList(
                    KConf.ExiledConfiguration.GetListStringValue(Config.GetString("util_guard_inventory",
                        null)));

                Plugin.APIKey = Plugin.Config.GetString("kings_apikey", null);
                Log.Info("Loaded Inventories.");
            }
            catch (Exception e)
            {
                Log.Error("Failed to add items to custom inventories! Check your inventory config values for errors!\n[EXCEPTION] For Developers:\n" + e);
                return;
            }
            try
			{
				Log.Debug("Initializing event handlers..");
				//Set instance varible to a new instance, this should be nulled again in OnDisable
				EventHandlers = new EventHandlers(roleHealth, Inventories);
                //Hook the events you will be using in the plugin. You should hook all events you will be using here, all events should be unhooked in OnDisabled 
                Events.PlayerJoinEvent += EventHandlers.OnPlayerConnect;
                Events.PlayerLeaveEvent += EventHandlers.OnPlayerDisconnect;
				Events.PlayerDeathEvent += EventHandlers.OnPlayerDeath;
				Events.RoundStartEvent += EventHandlers.OnRoundStart;
				Events.RemoteAdminCommandEvent += EventHandlers.OnCommand;
				Events.ConsoleCommandEvent += EventHandlers.OnConsoleCommand;
				Events.RoundEndEvent += EventHandlers.OnRoundEnd;
				Events.WaitingForPlayersEvent += EventHandlers.WaitingForPlayers;
				Events.TeamRespawnEvent += EventHandlers.OnTeamRespawn;
                Events.PlayerBannedEvent += EventHandlers.OnPlayerBanned;
				Log.Info($"KingsSCPSL plugin loaded. Written by Thomasjosif");
			}
			catch (Exception e)
			{
				//This try catch is redundant, as EXILED will throw an error before this block can, but is here as an example of how to handle exceptions/errors
				Log.Error($"There was an error loading the plugin: {e}");
			}

        }

		public override void OnDisable()
		{
			roleHealth.Clear();

            Events.PlayerJoinEvent -= EventHandlers.OnPlayerConnect;
            Events.PlayerLeaveEvent -= EventHandlers.OnPlayerDisconnect;
			Events.PlayerDeathEvent -= EventHandlers.OnPlayerDeath;
			Events.RoundStartEvent -= EventHandlers.OnRoundStart;
			Events.RemoteAdminCommandEvent -= EventHandlers.OnCommand;
			Events.ConsoleCommandEvent -= EventHandlers.OnConsoleCommand;
			Events.RoundEndEvent -= EventHandlers.OnRoundEnd;
			Events.WaitingForPlayersEvent -= EventHandlers.WaitingForPlayers;
			Events.TeamRespawnEvent -= EventHandlers.OnTeamRespawn;
            Events.PlayerBannedEvent -= EventHandlers.OnPlayerBanned;
            EventHandlers = null;

			Timing.KillCoroutines(cor);
		}

		public override void OnReload()
		{
			//This is only fired when you use the EXILED reload command, the reload command will call OnDisable, OnReload, reload the plugin, then OnEnable in that order. There is no GAC bypass, so if you are updating a plugin, it must have a unique assembly name, and you need to remove the old version from the plugins folder
		}

		public override string getName { get; } = "KingsSCPSL";
	}
    public partial class CustomInventory
    {
        public List<ItemType> NtfCadet = null;

        public List<ItemType> NtfLieutenant = null;

        public List<ItemType> NtfCommander = null;

        public List<ItemType> ClassD = null;

        public List<ItemType> Scientist = null;

        public List<ItemType> NtfScientist = null;

        public List<ItemType> Chaos = null;

        public List<ItemType> Guard = null;

        public Dictionary<ItemType, int> NtfCadetRan;
        public Dictionary<ItemType, int> NtfLtRan;
        public Dictionary<ItemType, int> NtfCmdRan;
        public Dictionary<ItemType, int> ClassDRan;
        public Dictionary<ItemType, int> ScientistRan;
        public Dictionary<ItemType, int> NtfSciRan;
        public Dictionary<ItemType, int> ChaosRan;
        public Dictionary<ItemType, int> GuardRan;

        public static List<ItemType> ConvertToItemList(List<string> list)
        {
            if (list == null)
                return new List<ItemType>();
            List<ItemType> listd = new List<ItemType>();
            foreach (string s in list)
            {
                listd.Add((ItemType)Enum.Parse(typeof(ItemType), s, true));
            }
            return listd;
        }

        public static Dictionary<ItemType, int> ConvertToRandomItemList(Dictionary<string, int> dict)
        {
            if (dict == null)
                return null;
            if (dict.ContainsKey("null"))
                return new Dictionary<ItemType, int>();
            Dictionary<ItemType, int> list = new Dictionary<ItemType, int>();
            foreach (string s in dict.Keys)
            {
                Log.Debug($"Adding item: {s}");
                list.Add((ItemType)Enum.Parse(typeof(ItemType), s, true), dict[s]);
            }

            return list;
        }

    }
}