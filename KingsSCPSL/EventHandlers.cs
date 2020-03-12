using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EXILED;
using EXILED.Extensions;
using GameCore;
using MEC;
using Mirror;
using RemoteAdmin;
using UnityEngine;
using Utf8Json.Resolvers.Internal;
using Log = EXILED.Log;
using Object = UnityEngine.Object;
using Harmony;
using static KingsSCPSL.Plugin;

namespace KingsSCPSL
{
	public class EventHandlers
	{
		public Plugin plugin;
		public EventHandlers(Plugin plugin) => this.plugin = plugin;

		private int iSCPCount = 0;
		private int iFacilityGuardCount = 0;
		private int iClassDCount = 0;
		private int iScientistCount = 0;
		private int iTotalPlayers = 0;
		private int iNotSpawnedCount = 0;

		private static string sRespawnQueue = "4014314031441404134041431430144144134414314031441441331441440131";
		char[] sRespawnQueueArray;
		
		private List<string> hubNotSpawnedList = new List<string>();
		private List<string> hubAFKList = new List<string>();
		private List<string> hubAFKToBeKickedList = new List<string>();
		public Dictionary<RoleType, int> RolesHealth;
		public CustomInventory Inventories;

		public EventHandlers(Dictionary<RoleType, int> health, CustomInventory inven)
		{
			RolesHealth = health;
			Inventories = inven;
		}
		public void OnPlayerDisconnect(PlayerLeaveEvent ev)
		{
			// Remove unused ID's from the lists because it would just stack up if we didn't.
			string userid = ev.Player.GetUserId();

			if (hubNotSpawnedList.Contains(userid))
				hubNotSpawnedList.Remove(userid);
			if (hubAFKList.Contains(userid))
				hubAFKList.Remove(userid);
			if (hubAFKToBeKickedList.Contains(userid))
				hubAFKToBeKickedList.Remove(userid);
		}

		public void OnRoundStart()
		{
			sRespawnQueueArray = sRespawnQueue.ToCharArray();
			iClassDCount = 0;
			iScientistCount = 0;
			iFacilityGuardCount = 0;
			iSCPCount = 0;
			iNotSpawnedCount = 0;
			hubNotSpawnedList.Clear();

			foreach (ReferenceHub hub in Player.GetHubs())
			{
				// Since this event fires before everyone has initially spawned, you need to wait before doing things like changing their health, adding items, etc
				Timing.RunCoroutine(CountSpawnedPlayers(hub));
			}
			Coroutines.Add(Timing.RunCoroutine(AFKCheckTimer()));
			Timing.RunCoroutine(CheckForSnap());
		}

		public IEnumerator<float> CountSpawnedPlayers(ReferenceHub hub)
		{
			// Wait 4 seconds to make sure everyone is spawned in correctly
			yield return Timing.WaitForSeconds(4f);

			switch (hub.characterClassManager.CurClass)
			{
				case RoleType.ClassD:
					iTotalPlayers++;
					iClassDCount++;
					break;
				case RoleType.Scientist:
					iTotalPlayers++;
					iScientistCount++;
					break;
				case RoleType.Scp049:
				case RoleType.Scp079:
				case RoleType.Scp106:
				case RoleType.Scp173:
				case RoleType.Scp096:
				case RoleType.Scp93953:
				case RoleType.Scp93989:
					iTotalPlayers++;
					iSCPCount++;
					break;
				case RoleType.FacilityGuard:
					iTotalPlayers++;
					iFacilityGuardCount++;
					break;
				case RoleType.Spectator:
					if (!hub.GetOverwatch())
					{
						iTotalPlayers++;
						iNotSpawnedCount++;
						hubNotSpawnedList.Add(hub.GetUserId());
					}
					break;
			}
		}

		public IEnumerator<float> CheckForSnap()
		{

			//Wait 8 seconds to make sure everyone is counted
			yield return Timing.WaitForSeconds(8f);
			var random = new System.Random();

			for (int i = 0; i <= iTotalPlayers; i++)
			{
				if (hubNotSpawnedList.Count <= 0)
					break;
				else
					Log.Info("Server snap detected!");
				int teamnum = (int)Char.GetNumericValue(sRespawnQueueArray[0]);
				bool tospawn = false;
				RoleType roletospawn = RoleType.None;

				switch (teamnum)
				{
					case 0:
						{
							if (iSCPCount > 0)
							{
								iSCPCount--;
							}
							else
							{
								tospawn = true;
								int scprand = random.Next(0, 4);
								switch(scprand)
								{
									case 0:
										roletospawn = RoleType.Scp049;
										break;
									case 1:
										roletospawn = RoleType.Scp096;
										break;
									case 2:
										roletospawn = RoleType.Scp173;
										break;
									case 3:
										roletospawn = RoleType.Scp106;
										break;
									case 4:
										roletospawn = RoleType.Scp079;
										break;
								}
							}
							break;
						}
					case 1:
						{
							if (iFacilityGuardCount > 0)
							{
								iFacilityGuardCount--;
							}
							else
							{
								tospawn = true;
								roletospawn = RoleType.FacilityGuard;
							}
							break;
						}
					case 3:
						{
							if (iScientistCount > 0)
							{
								iScientistCount--;
							}
							else
							{
								tospawn = true;
								roletospawn = RoleType.Scientist;
							}
							break;
						}
					case 4:
						{
							if (iClassDCount > 0)
							{
								iClassDCount--;
							}
							else
							{
								tospawn = true;
								roletospawn = RoleType.ClassD;
							}
							break;
						}
				}
				sRespawnQueueArray = RemoveFromArrayAt(sRespawnQueueArray, 0);
				if (tospawn)
				{
					int index;
					ReferenceHub playertospawn;
					for(; ; )
					{
						index = random.Next(hubNotSpawnedList.Count());
						playertospawn = Player.GetPlayer(hubNotSpawnedList[index]);
						hubNotSpawnedList.Remove(hubNotSpawnedList[index]);
						if (playertospawn.characterClassManager.CurClass == RoleType.Spectator && playertospawn != null && !playertospawn.GetOverwatch())
						{
							Log.Info($"Spawning in valid player {playertospawn.nicknameSync.MyNick} as {Enum.GetName(typeof(RoleType), roletospawn)}");

							playertospawn.characterClassManager.SetClassID(roletospawn);
							playertospawn.Broadcast(10, $"Since you didn't spawn naturally you were put in as a {Enum.GetName(typeof(RoleType), roletospawn)}", false);
							playertospawn.SetHealth(RolesHealth[roletospawn]);
							GiveSpawnItemsToPlayer(playertospawn);
							break;
						}
					}
				}

			}
		}
		private IEnumerator<float> AFKCheckTimer()
		{
			for (; ; )
			{
				yield return Timing.WaitForSeconds(10f);
				foreach (GameObject o in PlayerManager.players)
				{
					ReferenceHub rh = o.GetComponent<ReferenceHub>();
					string userid = rh.GetUserId(); 
					if(hubAFKList.Contains(userid))
					{
						rh.Broadcast(12, "You are in <color=blue>OVERWATCH MODE</color> because you were AFK. Please type \".back\" in console to be removed from overwatch.", false);
						if (!rh.GetOverwatch())
							rh.SetOverwatch(true);
					}
					if (hubAFKToBeKickedList.Contains(userid))
					{
						rh.Broadcast(12, "You are in <color=blue>OVERWATCH MODE</color> because you were AFK. Please type \".back\" in console to be removed from overwatch. <color=red> YOU WILL BE AUTOMATICALLY KICKED NEXT MTF SPAWN IF YOU REMAIN AFK!</color>", false);
						if (!rh.GetOverwatch())
							rh.SetOverwatch(true);
					}
				}
			}
		}

		public void OnTeamRespawn(ref TeamRespawnEvent ev)
		{
			foreach (GameObject o in PlayerManager.players)
			{
				ReferenceHub rh = o.GetComponent<ReferenceHub>();
				string userid = rh.GetUserId();
				if (hubAFKToBeKickedList.Contains(userid))
				{
					// User has been AFK for 2 MTF/Chaos respawns, kick now.
					hubAFKToBeKickedList.Remove(userid);
					ServerConsole.Disconnect(o, "You were automatically kicked for being AFK for more than 2 team respawns!");

				}
				if (hubAFKList.Contains(userid))
				{
					// User has been AFK for 1 MTF/Chaos respawn.
					hubAFKList.Remove(userid);
					hubAFKToBeKickedList.Add(userid);
					rh.Broadcast(12, "You are in <color=blue>OVERWATCH MODE</color> because you were AFK. Please type \".back\" in console to be removed from overwatch. <color=red> YOU WILL BE AUTOMATICALLY KICKED SOON!</color>", false);
				}
			}
		}

		public void OnPlayerDeath(ref PlayerDeathEvent ev)
		{
			if (ev.Killer != null && ev.Player != null)
			{ 
				foreach (GameObject o in PlayerManager.players)
				{
					ReferenceHub rh = o.GetComponent<ReferenceHub>();
					if (rh.serverRoles.RemoteAdmin)
						rh.queryProcessor.TargetReply(rh.characterClassManager.connectionToClient, $"KingsSCPSL#{ev.Info.Attacker} ({ev.Killer.characterClassManager.CurClass}) killed {ev.Player.nicknameSync.MyNick} - {ev.Player.characterClassManager.UserId} ({ev.Player.characterClassManager.CurClass}) with {DamageTypes.FromIndex(ev.Info.Tool)}.", true, true, string.Empty);

				}
			}
		}

		public void OnCommand(ref RACommandEvent ev)
		{
			try
			{
				if (ev.Command.Contains("REQUEST_DATA PLAYER_LIST SILENT"))
					return;

				// Lots of this logging shit and generic formatting was stolen from admin-tools by the exiled devs.
				string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
				string scpFolder = Path.Combine(appData, "SCP Secret Laboratory");
				string logs = Path.Combine(scpFolder, "AdminLogs");
				string fileName = Path.Combine(logs, $"command_log-{ServerConsole.Port}.txt");
				if (!Directory.Exists(logs))
					Directory.CreateDirectory(logs);
				if (!File.Exists(fileName))
					File.Create(fileName).Close();
				string data =
					$"{DateTime.Now}: {ev.Sender.Nickname} ({ev.Sender.SenderId}) executed: {ev.Command} {Environment.NewLine}";
				File.AppendAllText(fileName, data);

				string[] args = ev.Command.Split(' ');
				ReferenceHub sender = ev.Sender.SenderId == "SERVER CONSOLE" || ev.Sender.SenderId == "GAME CONSOLE" ? Player.GetPlayer(PlayerManager.localPlayer) : Player.GetPlayer(ev.Sender.SenderId);

				switch (args[0].ToLower())
				{
					case "afkcheck":
						{
							ev.Allow = false;
							if (!sender.CheckPermission("king.afk"))
							{
								ev.Sender.RAMessage("Permission denied.");
								return;
							}
							ReferenceHub rh = Player.GetPlayer(string.Join(" ", args.Skip(1)));
							if (rh == null)
							{
								ev.Sender.RAMessage("Player not found.", false);
								return;
							}
							if (rh.characterClassManager.CurClass != RoleType.Spectator)
							{
								rh.Broadcast(12, "You are in <color=blue>OVERWATCH MODE</color> because you were AFK. Please type \".back\" in console to be removed from overwatch.", false);
								rh.characterClassManager.SetClassID(RoleType.Spectator);
								rh.SetOverwatch(true);
								hubAFKList.Add(rh.GetUserId());
							}
							else
							{
								ev.Sender.RAMessage("Player is already in spectator! Please run the AFK command on non-spectating players!");
							}
							return;
						}
				}
			}
			catch (Exception e)
			{
				Log.Error($"Handling command error: {e}");
			}
		}

		public void OnConsoleCommand(ConsoleCommandEvent ev)
		{
			try
			{
				switch (ev.Command)
				{
					case "back":
						{
							Log.Info("Back command ran");
							string userid = ev.Player.GetUserId();
							if (hubAFKList.Contains(userid))
								hubAFKList.Remove(userid);
							if (hubAFKToBeKickedList.Contains(userid))
								hubAFKToBeKickedList.Remove(userid);
							ev.Player.SetOverwatch(false);

							ev.ReturnMessage = "[KingsSCPSL] You have been removed from the AFK list!";
							break;
						}
				}
			}
			catch (Exception e)
			{
				Log.Error($"Handling command error: {e}");
			}
		}

		public void OnRoundEnd()
		{
			foreach (CoroutineHandle handle in Coroutines)
				Timing.KillCoroutines(handle);
		}

		public void OnWaitingForPlayers()
		{
			foreach (CoroutineHandle handle in Coroutines)
				Timing.KillCoroutines(handle);
		}

		static char[] RemoveFromArrayAt(char[] source, int removeAt)
		{
			if (source == null || removeAt > source.Length)
				return null;

			char[] result = new char[source.Length - 1];
			Array.Copy(source, result, removeAt);
			Array.Copy(source, removeAt + 1, result, removeAt, source.Length - removeAt - 1);

			return result;
		}

		public void GiveSpawnItemsToPlayer(ReferenceHub player)
		{
			player.inventory.Clear();
			switch (player.characterClassManager.CurClass)
			{
				case RoleType.ClassD:
					foreach (ItemType item in Inventories.ClassD)
					{
						player.inventory.AddNewItem(item);
					}
					player.ammoBox.SetOneAmount(0, "0");
					player.ammoBox.SetOneAmount(1, "0");
					player.ammoBox.SetOneAmount(2, "0");
					break;
				case RoleType.ChaosInsurgency:
					foreach (ItemType item in Inventories.Chaos)
					{
						player.inventory.AddNewItem(item);

					}
					player.ammoBox.SetOneAmount(1, "90");
					player.ammoBox.SetOneAmount(2, "250");
					player.ammoBox.SetOneAmount(3, "125");
					break;
				case RoleType.NtfCadet:
					foreach (ItemType item in Inventories.NtfCadet)
					{
						player.inventory.AddNewItem(item);

					}
					player.ammoBox.SetOneAmount(0, "120");
					player.ammoBox.SetOneAmount(1, "120");
					player.ammoBox.SetOneAmount(2, "250");
					break;
				case RoleType.NtfCommander:
					foreach (ItemType item in Inventories.NtfCommander)
					{
						player.inventory.AddNewItem(item);
					}
					player.ammoBox.SetOneAmount(0, "120");
					player.ammoBox.SetOneAmount(1, "0");
					player.ammoBox.SetOneAmount(2, "100");
					break;
				case RoleType.NtfLieutenant:
					foreach (ItemType item in Inventories.NtfLieutenant)
					{
						player.inventory.AddNewItem(item);

					}
					player.ammoBox.SetOneAmount(0, "250");
					player.ammoBox.SetOneAmount(1, "125");
					player.ammoBox.SetOneAmount(2, "125");
					break;
				case RoleType.NtfScientist:
					foreach (ItemType item in Inventories.NtfScientist)
					{
						player.inventory.AddNewItem(item);

					}
					player.ammoBox.SetOneAmount(0, "320");
					player.ammoBox.SetOneAmount(1, "125");
					player.ammoBox.SetOneAmount(2, "125");
					break;
				case RoleType.Scientist:
					foreach (ItemType item in Inventories.Scientist)
					{
						player.inventory.AddNewItem(item);

					}
					player.ammoBox.SetOneAmount(0, "0");
					player.ammoBox.SetOneAmount(1, "0");
					player.ammoBox.SetOneAmount(2, "0");
					break;
				case RoleType.FacilityGuard:
					foreach (ItemType item in Inventories.Guard)
					{
						player.inventory.AddNewItem(item);

					}
					player.ammoBox.SetOneAmount(0, "80");
					player.ammoBox.SetOneAmount(1, "145");
					player.ammoBox.SetOneAmount(2, "80");
					break;
			}
		}
	}
}