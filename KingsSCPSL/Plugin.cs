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
		public static List<CoroutineHandle> Coroutines = new List<CoroutineHandle>();

		public override void OnEnable()
		{
			try
			{
				Log.Debug("Initializing event handlers..");
				//Set instance varible to a new instance, this should be nulled again in OnDisable
				EventHandlers = new EventHandlers(this);
				//Hook the events you will be using in the plugin. You should hook all events you will be using here, all events should be unhooked in OnDisabled 
				Events.PlayerLeaveEvent += EventHandlers.OnPlayerDisconnect;
				Events.PlayerDeathEvent += EventHandlers.OnPlayerDeath;
				Events.RoundStartEvent += EventHandlers.OnRoundStart;
				Events.RemoteAdminCommandEvent += EventHandlers.OnCommand;
				Events.ConsoleCommandEvent += EventHandlers.OnConsoleCommand;
				Events.RoundEndEvent += EventHandlers.OnRoundEnd;
				Events.WaitingForPlayersEvent += EventHandlers.OnWaitingForPlayers;
				Events.TeamRespawnEvent += EventHandlers.OnTeamRespawn;
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
			Events.PlayerLeaveEvent -= EventHandlers.OnPlayerDisconnect;
			Events.PlayerDeathEvent -= EventHandlers.OnPlayerDeath;
			Events.RoundStartEvent -= EventHandlers.OnRoundStart;
			Events.RemoteAdminCommandEvent -= EventHandlers.OnCommand;
			Events.ConsoleCommandEvent -= EventHandlers.OnConsoleCommand;
			Events.RoundEndEvent -= EventHandlers.OnRoundEnd;
			Events.WaitingForPlayersEvent -= EventHandlers.OnWaitingForPlayers;
			Events.TeamRespawnEvent -= EventHandlers.OnTeamRespawn;
			EventHandlers = null;

			Timing.KillCoroutines(cor);
		}

		public override void OnReload()
		{
			//This is only fired when you use the EXILED reload command, the reload command will call OnDisable, OnReload, reload the plugin, then OnEnable in that order. There is no GAC bypass, so if you are updating a plugin, it must have a unique assembly name, and you need to remove the old version from the plugins folder
		}

		public override string getName { get; } = "KingsSCPSL";
	}
}