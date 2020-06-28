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
using static KingsSCPSL.PlayerManagement;
using System.Text;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib4Mirror;
using LiteNetLib.Utils;
using System.Diagnostics;

namespace KingsSCPSL
{
	internal enum KickReason : byte
	{
		Ban,
		IPBan
	}

	internal readonly struct PlayerToKick : IEquatable<PlayerToKick>
	{
		private readonly string userId;
		internal readonly KickReason reason;
		internal readonly uint creationTime;

		internal PlayerToKick(string userId, KickReason reason)
		{
			this.userId = userId;
			this.reason = reason;
			creationTime = (uint)EventHandlers.stopwatch.Elapsed.TotalSeconds;
		}

		public bool Equals(PlayerToKick other) =>
			string.Equals(userId, other.userId, StringComparison.InvariantCultureIgnoreCase);

		public override bool Equals(object obj) => obj is PlayerToKick other && Equals(other);

		public override int GetHashCode() =>
			userId != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(userId) : 0;
	}

	public class EventHandlers
	{
		public Plugin plugin;
		public EventHandlers(Plugin plugin) => this.plugin = plugin;

		public static string MSG_PREFIX = "<color=white>[</color><color=green>KingsSCPSL</color><color=white>]</color>";
		public static string BAN_MSG = "<size=70><color=red>You are banned. </color><size=70>Please visit <color=blue> https://bans.kingsplayground.fun/ </color>for more information.</size></size>";
		public static string BAN_MSG_IP = "<size=70><color=red>You are IP banned. </color><size=70>Please visit <color=blue> https://bans.kingsplayground.fun/ </color>for more information.</size></size>";
		private int iSCPCount = 0;
		private int iFacilityGuardCount = 0;
		private int iClassDCount = 0;
		private int iScientistCount = 0;
		private int iTotalPlayers = 0;
		private int iNotSpawnedCount = 0;
		private int iTotalChaos = 0;
		private bool bChaosOnStart = false;
		private int iMaxPlayers = 64;

		private bool bRoundStarted = false;
		private bool bSpectatorsMuted = false;

		private static string sRespawnQueue = "4014314031441404134041431430144144134414314031441441331441440131";
		char[] sRespawnQueueArray;

		private List<string> hubNotSpawnedList = new List<string>();
		private List<string> hubMutedList = new List<string>();

		public Dictionary<RoleType, int> RolesHealth;
		public Dictionary<string, long> ConnectTime = new Dictionary<string, long>();
		public CustomInventory Inventories;
		private const byte BypassFlags = (1 << 1) | (1 << 3); //IgnoreBans or IgnoreGeoblock

		internal static readonly Stopwatch stopwatch = new Stopwatch();
		private static readonly Stopwatch cleanupStopwatch = new Stopwatch();
		private static readonly HashSet<PlayerToKick> ToKick = new HashSet<PlayerToKick>();
		private static readonly HashSet<PlayerToKick> ToClear = new HashSet<PlayerToKick>();
		private static readonly NetDataReader reader = new NetDataReader();
		private static readonly NetDataWriter writer = new NetDataWriter();

		public EventHandlers(Dictionary<RoleType, int> health, CustomInventory inven)
		{
			RolesHealth = health;
			Inventories = inven;
		}
		public void OnPlayerConnect(PlayerJoinEvent ev)
		{
			_ = ConnectChecks(ev);
		}

		public void OnPreAuth(ref PreauthEvent ev)
		{
			_ = BanChecks(ev);
		}
		public async Task BanChecks(PreauthEvent ev)
		{
			Log.Info($" USERID: {ev.UserId} connected, performing banchecks!");
			if (await PlayerManagement.IsBanned(ev.UserId, false))
			{
				ReferenceHub hub = Player.GetPlayer(ev.UserId);
				if (hub != null)
					ServerConsole.Disconnect(hub.characterClassManager.connectionToClient,
						BAN_MSG);
				else
				{
					StartStopwatch();
					ToKick.Add(new PlayerToKick(ev.UserId, KickReason.Ban));
				}
				return;
			}
		}
		public async Task ConnectChecks(PlayerJoinEvent ev)
		{
			if (ToKick.TryGetValue(new PlayerToKick(ev.Player.characterClassManager.UserId, KickReason.Ban),
				out var tk))
			{
				ToKick.Remove(tk);
				ServerConsole.Disconnect(ev.Player.characterClassManager.connectionToClient,
					tk.reason == KickReason.Ban ? BAN_MSG : BAN_MSG_IP);
			}

			try
			{
				string group = await PlayerManagement.GetAdminRole(ev.Player.GetUserId());
				if (group != "")
				{
					ev.Player.SetRank(ServerStatic.GetPermissionsHandler()._groups[group]);
					if (!ServerStatic.GetPermissionsHandler()._members.ContainsKey(ev.Player.characterClassManager.UserId))
						ServerStatic.GetPermissionsHandler()._members.Add(ev.Player.characterClassManager.UserId, group);
					else
						ServerStatic.GetPermissionsHandler()._members[ev.Player.characterClassManager.UserId] = group;

					Log.Info($"Setting player rank now {ev.Player.nicknameSync.MyNick} ({group})");
				}
				else
					Log.Info($"No rank entries found for {ev.Player.nicknameSync.MyNick}");
			}
			catch (Exception e)
			{
				Log.Error($"Error while trying to add rank from web API to player! Exception: {e}");
			}
			long unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			ConnectTime[ev.Player.GetUserId()] = unixTimestamp;

			/*if (bSpectatorsMuted)
			{
				if (!ev.Player.CheckPermission("king.ignoremute"))
				{
					hubMutedList.Add(ev.Player.GetUserId());
					ev.Player.Mute();
					ev.Player.Broadcast(10, "[KingsSCPSL] Spectator Chat has been muted until round start to preserve order.", false);
				}
			}*/
			return;
		}
		public void OnPlayerDisconnect(PlayerLeaveEvent ev)
		{
			// Remove unused ID's from the lists because it would just stack up if we didn't.
			string userid = ev.Player.GetUserId();

			if (hubNotSpawnedList.Contains(userid))
				hubNotSpawnedList.Remove(userid);

			//TryReplacePlayer(ev.Player); Broken :(

			_ = DisconnectChecks(ev);

		}

		public async Task DisconnectChecks(PlayerLeaveEvent ev)
		{
			long unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			long connectTime = ConnectTime[ev.Player.GetUserId()];
			Log.Info($"[Playtime Checker] Logging Playtime for {ev.Player.GetUserId()} Connect: {connectTime} Disconnect: {unixTimestamp} Port: {ServerConsole.Port}");
			await PlayerManagement.UpdatePlaytime(ev.Player.GetUserId(), connectTime, unixTimestamp, ServerConsole.Port);
		}

		public void OnRoundStart()
		{
			bRoundStarted = true;
			sRespawnQueueArray = sRespawnQueue.ToCharArray();
			iClassDCount = 0;
			iScientistCount = 0;
			iFacilityGuardCount = 0;
			iSCPCount = 0;
			iNotSpawnedCount = 0;
			iTotalChaos = 0;
			bChaosOnStart = false;
			hubNotSpawnedList.Clear();

			foreach (ReferenceHub hub in Player.GetHubs())
			{
				// Since this event fires before everyone has initially spawned, you need to wait before doing things like changing their health, adding items, etc
				Timing.RunCoroutine(CountPlayer(hub));
			}
			Timing.RunCoroutine(CheckForSnap());
		}
		/***************************************************************************************************
		 ************************************* SERVER SNAP DETECTION ***************************************
		 ***************************************************************************************************/

		public IEnumerator<float> CountPlayer(ReferenceHub hub)
		{
			// Wait 4 seconds to make sure everyone is spawned in correctly
			yield return Timing.WaitForSeconds(4f);

			if (hub != null)
			{
				// Count spawned players and add to global variables.
				switch (hub.characterClassManager.CurClass)
				{
					case RoleType.ChaosInsurgency:
						bChaosOnStart = true;
						iTotalChaos++;
						break;
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
							// Player was not spawned (or latejoined) and will need to be spawned manually
							iTotalPlayers++;
							iNotSpawnedCount++;
							hubNotSpawnedList.Add(hub.GetUserId());
						}
						break;
				}
			}
		}

		public IEnumerator<float> CheckForSnap()
		{
			// We wait for 8 seconds to verify that everyone has spawned and been counted by the above function
			yield return Timing.WaitForSeconds(8f);
			var random = new System.Random();

			if (hubNotSpawnedList.Count > 0)
				Log.Info($"Server snap detected! We have {hubNotSpawnedList.Count} non-spawned players!");

			// Iterate over the total number of players counted
			for (int i = 0; i < iTotalPlayers; i++)
			{
				// If everyone has been spawned, stop looping!
				if (hubNotSpawnedList.Count <= 0)
					break;

				int teamnum = (int)Char.GetNumericValue(sRespawnQueueArray[0]);
				bool tospawn = false;
				RoleType roletospawn = RoleType.None;

				switch (teamnum)
				{
					// SCP
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
								switch (scprand)
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
					// Chaos / Facility
					case 1:
						{
							if (bChaosOnStart)
							{
								if (iTotalChaos > 0)
								{
									iTotalChaos--;
								}
								else
								{
									tospawn = true;
									roletospawn = RoleType.ChaosInsurgency;
								}
								break;
							}
							else
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
						}
					// Scientist
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
					// Class D
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

					// Loop over max clients till we find a replacement
					for (int j = 0; j < iMaxPlayers; j++)
					{
						index = random.Next(hubNotSpawnedList.Count());
						playertospawn = Player.GetPlayer(hubNotSpawnedList[index]);
						hubNotSpawnedList.Remove(hubNotSpawnedList[index]);
						if (playertospawn.characterClassManager.CurClass == RoleType.Spectator && playertospawn != null && !playertospawn.GetOverwatch())
						{

							Log.Info($"Spawning in valid player {playertospawn.nicknameSync.MyNick} as {Enum.GetName(typeof(RoleType), roletospawn)}");

							playertospawn.characterClassManager.SetClassID(roletospawn);
							playertospawn.Broadcast(10, $"{MSG_PREFIX} Since you didn't spawn naturally you were put in as a {Enum.GetName(typeof(RoleType), roletospawn)}", false);
							playertospawn.SetHealth(RolesHealth[roletospawn]);
							GiveSpawnItemsToPlayer(playertospawn);
							break;
						}
					}
				}

			}

			// Second pass to make sure we have all the required SCP's Spawned!
			sRespawnQueueArray = sRespawnQueue.ToCharArray();
			iTotalPlayers = 0;
			iSCPCount = 0;
			int iNeededSCPCount = 0;

			foreach (GameObject o in PlayerManager.players)
			{
				ReferenceHub rh = o.GetComponent<ReferenceHub>();

				if (rh != null)
				{

					iTotalPlayers++;
					switch (rh.characterClassManager.CurClass)
					{
						case RoleType.Scp049:
						case RoleType.Scp079:
						case RoleType.Scp106:
						case RoleType.Scp173:
						case RoleType.Scp096:
						case RoleType.Scp93953:
						case RoleType.Scp93989:
							iSCPCount++;
							Log.Info($"Found {rh.GetUserId()} (SCP)");
							break;
						default:
							Log.Info($"Found {rh.GetUserId()} (Not SCP)");
							break;
					}
				}
			}


			for (int i = 0; i < iTotalPlayers; i++)
			{
				int teamnum = (int)Char.GetNumericValue(sRespawnQueueArray[0]);

				if (teamnum == 0)
				{
					iNeededSCPCount++;
				}
				sRespawnQueueArray = RemoveFromArrayAt(sRespawnQueueArray, 0);
			}

			Log.Info($"Needed SCP's: {iNeededSCPCount} SCP's Counted: {iSCPCount}");
			if (iSCPCount < iNeededSCPCount)
			{
				Log.Info("Not enough SCP's Spawned! Correcting now.");
				for (int i = 0; i <= (iNeededSCPCount - iSCPCount); i++)
				{
					ReferenceHub playertospawn;
					RoleType roletospawn = RoleType.None;
					for (int j = 0; j < iMaxPlayers; j++)
					{
						playertospawn = GetRandomPlayer();
						if (playertospawn != null && !playertospawn.GetOverwatch() && NotSCP(playertospawn))
						{
							int scprand = random.Next(0, 4);
							switch (scprand)
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
							Log.Info($"Spawning in valid player {playertospawn.nicknameSync.MyNick} as {Enum.GetName(typeof(RoleType), roletospawn)} to fix the SCP Count!");

							playertospawn.SetRole(roletospawn);
							playertospawn.Broadcast(10, $"{MSG_PREFIX} Since not enough SCP's spawned your were put in as a {Enum.GetName(typeof(RoleType), roletospawn)}", false);
							playertospawn.SetHealth(RolesHealth[roletospawn]);
							break;
						}
					}
				}
			}

			/*foreach (GameObject o in PlayerManager.players)
			{
				ReferenceHub hub = o.GetComponent<ReferenceHub>();
				if (isDeveloper(hub))
				{
					hub.SetRankName("KILL TARGET");
					hub.SetvvRankColor("pink");
				}
			}*/
		}

		/***************************************************************************************************
		 ************************************* END SERVER SNAP DETECTION ***********************************
		 ***************************************************************************************************/
		public void OnTeamRespawn(ref TeamRespawnEvent ev)
		{
		}

		public void OnPlayerDeath(ref PlayerDeathEvent ev)
		{
			if (ev.Killer != null && ev.Player != null)
			{
				foreach (GameObject o in PlayerManager.players)
				{
					ReferenceHub rh = o.GetComponent<ReferenceHub>();
					if (rh.serverRoles.RemoteAdmin)
						rh.queryProcessor.TargetReply(rh.characterClassManager.connectionToClient, $"KingsSCPSL#{ev.Info.Attacker} ({ev.Killer.characterClassManager.CurClass}) killed {ev.Player.nicknameSync.MyNick} - {ev.Player.characterClassManager.UserId} ({ev.Player.characterClassManager.CurClass}) with {ev.Info.Tool}.", true, true, string.Empty);

				}
			}
		}

		public void OnPlayerBanned(PlayerBannedEvent ev)
		{
			_ = DoBan(ev);
		}

		public async Task DoBan(PlayerBannedEvent ev)
		{

			string banned_user_id = ev.Details.Id;
			double banduration = TimeSpan.FromTicks(ev.Details.Expires - ev.Details.IssuanceTime).TotalSeconds;
			ReferenceHub adminHub = Player.GetPlayer(ev.Details.Issuer);
			string adminId = await PlayerManagement.GetAdminID(adminHub.GetUserId());

			Log.Info("Ban DETECTED:");
			Log.Info($"Banned ID: {banned_user_id}");
			Log.Info($"Admin Name: {adminHub.nicknameSync.MyNick}");
			Log.Info($"Admin ID: {adminId}");
			Log.Info($"Duration: {banduration}");
			if (banduration.ToString().Contains("1576800000"))
			{
				banduration = 0;
				Log.Info($"Duration UPDATED TO PERM!");
			}

			if (await PlayerManagement.IssueBan(banned_user_id, ev.Details.OriginalName, adminId, banduration.ToString(), ev.Type, ev.Details.Reason))
			{
				Log.Info($"Successfully pushed ban for {banned_user_id} to the web API!");

				// We can safely remove the ban since the web client will handle it from here.
				BanHandler.RemoveBan(ev.Details.Id, ev.Type);
			}
			else
			{
				// Error out to requesting admin
				adminHub.Broadcast(15, $"ERROR while adding ban to web API for: {ev.Details.OriginalName}({banned_user_id})", false);
				Log.Error($"FATAL BANNING ERROR! PlayerManagement.IssueBan() Failed to push to web API");
			}

		}

		public void OnCommand(ref RACommandEvent ev)
		{
			/*try
			{
				if (ev.Command.Contains("REQUEST_DATA PLAYER_LIST SILENT"))
					return;

				// Lots of this logging shit and generic formatting was stolen from admin-tools by the exiled devs.
				string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
				string scpFolder = Path.Combine(apvpData, "SCP Secret Laboratory");
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
					case "muteall":
						{
							ev.Allow = false;
							if (!sender.CheckPermission("king.muteall"))
							{
								ev.Sender.RAMessage("Permission denied.");
								return;
							}
							int timeinseconds = 30;
							if (args[1] != null)
							{
								if (!Int32.TryParse(args[1], out timeinseconds))
								{
									timeinseconds = 30;
								}
							}
							foreach (GameObject o in PlayerManager.players)
							{
								ReferenceHub rh = o.GetComponent<ReferenceHub>();
								if (!rh.CheckPermission("king.ignoremute"))
								{
									if (rh.characterClassManager.CurClass == RoleType.Spectator || rh.characterClassManager.CurClass == RoleType.None)
									{
										rh.Broadcast(12, $"Spectator chat has been muted for {timeinseconds} ", false);
										rh.characterClassManager.SetClassID(RoleType.Spectator);
										rh.SetOverwatch(true);
									}
								}
							}
							return;
						}
				}
			}
			catch (Exception e)
			{
				Log.Error($"Handling command error: {e}");
			}*/
		}

		public void OnConsoleCommand(ConsoleCommandEvent ev)
		{
			/* No longer used.
			 * try
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
			}*/
		}

		public void OnRoundEnd()
		{
			ToKick.Clear();
			stopwatch.Reset();
			cleanupStopwatch.Reset();

			foreach (CoroutineHandle handle in Coroutines)
				Timing.KillCoroutines(handle);
		}

		public void WaitingForPlayers()
		{
			NineTailedFoxUnits.host.list.Add("<color=red>King's Playground</color>");
			NineTailedFoxUnits.host.list.Add("<size=20><color=blue>https://discord.gg/hqfESe9</color></size>");
			bSpectatorsMuted = false;
			bRoundStarted = false;
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
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 0);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 0);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 0);
					break;
				case RoleType.ChaosInsurgency:
					foreach (ItemType item in Inventories.Chaos)
					{
						player.inventory.AddNewItem(item);

					}
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 90);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 250);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 125);
					break;
				case RoleType.NtfCadet:
					foreach (ItemType item in Inventories.NtfCadet)
					{
						player.inventory.AddNewItem(item);

					}
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 120);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 120);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 250);
					break;
				case RoleType.NtfCommander:
					foreach (ItemType item in Inventories.NtfCommander)
					{
						player.inventory.AddNewItem(item);
					}
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 120);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 0);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 100);
					break;
				case RoleType.NtfLieutenant:
					foreach (ItemType item in Inventories.NtfLieutenant)
					{
						player.inventory.AddNewItem(item);

					}
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 250);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 125);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 125);
					break;
				case RoleType.NtfScientist:
					foreach (ItemType item in Inventories.NtfScientist)
					{
						player.inventory.AddNewItem(item);

					}
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 320);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 125);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 125);
					break;
				case RoleType.Scientist:
					foreach (ItemType item in Inventories.Scientist)
					{
						player.inventory.AddNewItem(item);

					}
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 0);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 0);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 0);
					break;
				case RoleType.FacilityGuard:
					foreach (ItemType item in Inventories.Guard)
					{
						player.inventory.AddNewItem(item);

					}
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped5, 80);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped7, 145);
					player.SetAmmo(EXILED.ApiObjects.AmmoType.Dropped9, 80);
					break;
			}
		}

		public static bool NotSCP(ReferenceHub player)
		{
			switch (player.characterClassManager.CurClass)
			{
				case RoleType.Scp049:
				case RoleType.Scp079:
				case RoleType.Scp106:
				case RoleType.Scp173:
				case RoleType.Scp096:
				case RoleType.Scp93953:
				case RoleType.Scp93989:
					return false;
				default:
					return true;
			}
		}

		public static ReferenceHub GetRandomPlayer()
		{
			var random = new System.Random();
			return PlayerManager.players[random.Next(PlayerManager.players.Count)].GetComponent<ReferenceHub>();
		}

		public static bool isDeveloper(ReferenceHub hub)
		{
			if (hub.GetUserId() == "thomasjosif@northwood")
			{
				return true;
			}
			return false;
		}

		/*public static void TryReplacePlayer(ReferenceHub replacing)
		{
			Log.Debug($"[Tryreplace] Tryreplace now on {replacing} currently {ev.Killer.characterClassManager.CurClass}");

			if (replacing.characterClassManager.CurClass != RoleType.Spectator && replacing.characterClassManager.CurClass != RoleType.None && replacing.characterClassManager.CurClass != RoleType.Tutorial)
			{
				Log.Debug($"[Tryreplace] ATTEMPTING REPLACE ON {replacing.nicknameSync.MyNick}");
				// Credit: DCReplace :)

				Inventory.SyncListItemInfo items = replacing.inventory.items;
				RoleType role = replacing.GetRole();
				Vector3 pos = replacing.transform.position;
				float health = replacing.GetHealth();

				// New strange ammo system because the old one was fucked.
				Dictionary<EXILED.ApiObjects.AmmoType, uint> ammo = new Dictionary<EXILED.ApiObjects.AmmoType, uint>();
				foreach (EXILED.ApiObjects.AmmoType atype in (EXILED.ApiObjects.AmmoType[])Enum.GetValues(typeof(EXILED.ApiObjects.AmmoType)))
				{
					ammo.Add(atype, replacing.GetAmmo(atype));
				}

				// Stuff for 079
				byte Level079 = 0;
				float Exp079 = 0f, AP079 = 0f;
				if (replacing.characterClassManager.CurClass == RoleType.Scp079)
				{
					Level079 = Convert.ToByte(Scp079.GetLevel(replacing));
					Exp079 = Scp079.GetExperience(replacing);
					AP079 = Scp079.GetEnergy(replacing);
				}

				for (; ; )
				{
					ReferenceHub player = GetRandomPlayer();
					if (player.GetRole() == RoleType.Spectator && player.characterClassManager.UserId != string.Empty && !player.GetOverwatch() && player != replacing)
					{
						Log.Info($"[TryReplace] Replacing player {replacing.nicknameSync.MyNick} with player {player.nicknameSync.MyNick}");
						player.SetRole(role);
						Timing.CallDelayed(0.3f, () =>
						{
							player.SetPosition(pos);
							player.inventory.Clear();
							foreach (var item in items) player.inventory.AddNewItem(item.id);
							player.SetHealth(health);

							foreach (EXILED.ApiObjects.AmmoType atype in (EXILED.ApiObjects.AmmoType[])Enum.GetValues(typeof(EXILED.ApiObjects.AmmoType)))
							{
								uint amount;
								if (ammo.TryGetValue(atype, out amount))
								{
									replacing.SetAmmo(atype, amount);
								}
								else
									Log.Error($"[Kings-SCP Plugin] ERROR: Tried to get a value from dict that did not exist! (Ammo)");
							}
							if (replacing.characterClassManager.CurClass == RoleType.Scp079)
							{
								Scp079.SetLevel(player, Level079, false);
								Scp079.SetExperience(player, Exp079);
								Scp079.SetEnergy(player, AP079);
							}
							player.Broadcast(10, $"{MSG_PREFIX} You have replaced a player who has disconnected.", false);
						});
					}
				}
			}
		}*/

		// Taken from VPNShield
		private static void StartStopwatch()
		{
			if (stopwatch.IsRunning)
			{
				if (ToKick.Count <= 500 || cleanupStopwatch.ElapsedMilliseconds <= 240000) return;

				//ToKick cleanup
				ToClear.Clear();
				uint secs = (uint)stopwatch.Elapsed.TotalSeconds - 180;

				foreach (PlayerToKick player in ToKick)
					if (player.creationTime < secs)
						ToClear.Add(player);

				foreach (PlayerToKick player in ToClear)
					ToKick.Remove(player);

				ToClear.Clear();
				cleanupStopwatch.Restart();

				return;
			}

			stopwatch.Start();
			cleanupStopwatch.Start();
		}
	}
}