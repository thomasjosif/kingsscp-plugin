using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MEC;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;
using static KingsSCPSL.MainClass;
using static KingsSCPSL.WebTask;
using System.Text;
using System.Threading.Tasks;
using LiteNetLib.Utils;
using System.Diagnostics;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.Events.EventArgs;
using Exiled.Permissions.Extensions;
using Hints;
using static Exiled.Permissions.Permissions;
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
			creationTime = (uint)PlayerEvents.stopwatch.Elapsed.TotalSeconds;
		}

		public bool Equals(PlayerToKick other) =>
			string.Equals(userId, other.userId, StringComparison.InvariantCultureIgnoreCase);

		public override bool Equals(object obj) => obj is PlayerToKick other && Equals(other);

		public override int GetHashCode() =>
			userId != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(userId) : 0;
	}

	public class PlayerEvents
	{
		public MainClass plugin;
		public PlayerEvents(MainClass plugin)
		{
			this.plugin = plugin;
		}

		public static string MSG_PREFIX = "<color=white>[</color><color=green>KingsSCPSL</color><color=white>]</color>";
		public static string BAN_MSG = "<size=70><color=red>You are banned. </color><size=70>Please visit <color=blue> https://bans.kingsplayground.fun/ </color>for more information.</size></size>";
		public static string BAN_MSG_IP = "<size=70><color=red>You are IP banned. </color><size=70>Please visit <color=blue> https://bans.kingsplayground.fun/ </color>for more information.</size></size>";

		private const byte BypassFlags = (1 << 1) | (1 << 3); //IgnoreBans or IgnoreGeoblock

		internal static readonly Stopwatch stopwatch = new Stopwatch();
		private static readonly Stopwatch cleanupStopwatch = new Stopwatch();
		private static readonly HashSet<PlayerToKick> ToKick = new HashSet<PlayerToKick>();
		private static readonly HashSet<PlayerToKick> ToClear = new HashSet<PlayerToKick>();
		private static readonly NetDataReader reader = new NetDataReader();
		private static readonly NetDataWriter writer = new NetDataWriter();
		private static bool bAlternate = false;

		public struct PlayerInfo
		{
			public bool needsCreating;
			public long connectTime;
			public bool isStaff;
			public bool isTop20;
			public bool isBanned;
			public string badgeText;
			public string badgeColor;
			public string adminRank;
		}

		public static Dictionary<string, PlayerInfo> PlayerInfoDict = new Dictionary<string, PlayerInfo>();

		public struct CommandLog
		{
			public string adminId;
			public string commandName;
			public string commandTargets;
			public string commandArgs;
		}

		public static List<CommandLog> logList = new List<CommandLog>();
		/******************************************************************************
						   EVENTS
		******************************************************************************/
		public void OnPlayerConnect(JoinedEventArgs ev)
		{
			_ = ConnectChecks(ev);
		}

		public void OnPreAuth(PreAuthenticatingEventArgs ev)
		{
			_ = BanChecks(ev);
		}
		public void OnPlayerDisconnect(LeftEventArgs ev)
		{
			// Remove unused ID's from the lists because it would just stack up if we didn't.
			string userid = ev.Player.UserId;

			if (SnapDetection.hubNotSpawnedList.Contains(userid))
				SnapDetection.hubNotSpawnedList.Remove(userid);

			_ = DisconnectChecks(ev);

		}
		public void OnPlayerHurt(HurtingEventArgs ev)
		{
			if (ev.Amount >= ev.Target.Health)
			{
				string kosinfo = "<color=white>";
				string detainedinfo = "<color=white>";
				if (ev.Target.Role == RoleType.ClassD && SnapDetection.NotSCP(ev.Attacker) && ev.Target.Role != ev.Attacker.Role)
					kosinfo = "<color=red>****POSSIBLE KOS****<color=white> ";
				if (ev.Target.IsCuffed && SnapDetection.NotSCP(ev.Attacker) && ev.Target.Role != ev.Attacker.Role)
					detainedinfo = "<color=blue>****DETAINED KILL****<color=white> ";

				foreach (Exiled.API.Features.Player ply in Exiled.API.Features.Player.List)
				{
					if (ply.ReferenceHub.serverRoles.RemoteAdmin)
						ply.RemoteAdminMessage($"{kosinfo}{detainedinfo}{ev.Attacker.Nickname} {ev.Attacker.UserId} ({ev.Attacker.Role}) killed {ev.Target.Nickname} ({ev.Target.Role}) with {DamageTypes.FromIndex(ev.HitInformations.Tool).name}.</color>", true, "KingsSCPSL");

				}
			}
		}
		public void OnPreBan(BanningEventArgs ev)
		{
			// This is a backup if the website is down and the plugin is blocking bans. The admin will have to set the reason to localban for it to override.
			if(ev.Reason == "localban")
			{
				ev.IsAllowed = true;
				ev.Issuer.Broadcast(10, "You banned the player with a localban, this does not affect the entire network!");
			}
			else
			{
				_ = DoBan(ev);
				ev.IsAllowed = false;
			}
		}

		public void OnPreKick(KickingEventArgs ev)
		{
			_ = DoKick(ev);
			ev.IsAllowed = false;
		}
		public void OnPlayerBanned(BannedEventArgs ev)
		{
			//_ = DoBan(ev);
		}
		public void OnCommand(SendingRemoteAdminCommandEventArgs ev)
		{
			var ignoreCommands = new List<string>() { "request_data" };
			var playerCommands = new List<string>() { "forceclass", "give", "overwatch", "god", "bypass", "bring",
										"goto", "heal", "noclip", "doortp", "effect", "mute", "unmute",
				                        "imute", "iunmute", "disarm", "release" };

			if(ignoreCommands.Contains(ev.Name.ToLower()))
			{
				// pass
			}
			else if(playerCommands.Contains(ev.Name.ToLower()))
			{
				var playerList = new List<int>();
				playerList = Misc.ProcessRaPlayersList(ev.Arguments[0]);

				string useridlist = "";
				foreach(int ply in playerList)
				{
					Player player = Player.Get(ply);
					useridlist = useridlist + player.UserId + ",";
				}

				string args = "";
				for(int i=1; i < ev.Arguments.Count; i++)
				{
					args = args + ev.Arguments[i] + " ";
				}

				CommandLog log;
				log.adminId = ev.Sender.UserId;
				log.commandName = ev.Name;
				log.commandTargets = useridlist;
				log.commandArgs = args;

				logList.Add(log);
			}
			else
			{
				string args = "";
				for (int i = 0; i < ev.Arguments.Count; i++)
				{
					args = args + ev.Arguments[i] + " ";
				}

				CommandLog log;
				log.adminId = ev.Sender.UserId;
				log.commandName = ev.Name;
				log.commandTargets = null;
				log.commandArgs = args;

				logList.Add(log);
			}
		}

		public void OnConsoleCommand(SendingConsoleCommandEventArgs ev)
		{
			if (ev.Name.ToLower().StartsWith("eh"))
			{
				ev.Player.ReferenceHub.hints.Show(new TextHint("Test Hints", new HintParameter[]
				{
									new StringHintParameter("")
				}, HintEffectPresets.FadeInAndOut(0.25f, 1f, 0f)));
			}
			/* No longer used.
			 * try
			{
				switch (ev.Command)
				{
					case "back":
						{
							Log.Info("Back command ran");
							string userid = ev.Player.UserId;
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
		/******************************************************************************
						   ASYNC EVENT HANDLING
		******************************************************************************/

		public async Task ConnectChecks(JoinedEventArgs ev)
		{
			if (ToKick.TryGetValue(new PlayerToKick(ev.Player.UserId, KickReason.Ban),
				out var tk))
			{
				ToKick.Remove(tk);
				SendClientToServer(ev.Player, 7790);
			}

			PlayerInfo pinfo;
			if (PlayerInfoDict.TryGetValue(ev.Player.UserId, out pinfo))
			{
				if (pinfo.needsCreating)
				{
					await WebTask.CreatePlayer(plugin.Config.APIKey, ev.Player.UserId, ev.Player.Nickname, ev.Player.ReferenceHub.serverRoles.DoNotTrack);
					return;
				}
				if (pinfo.isStaff)
				{
					UserGroup ug = ServerStatic.GetPermissionsHandler().GetGroup(pinfo.adminRank);
					ev.Player.SetRank(pinfo.adminRank, ug);
					string badgetext = ug.BadgeText;
					switch (pinfo.adminRank)
					{
						case "owner":
							{
								pinfo.badgeText = "OWNER";
								break;
							}
						case "doa":
							{
								pinfo.badgeText = "O5-COUNCIL (DIRECTOR)";
								break;
							}
						case "management":
							{
								pinfo.badgeText = "SERVER MANAGER";
								break;
							}
						case "headadmin":
							{
								pinfo.badgeText = "COMMANDER (HEAD-ADMIN)";
								break;
							}
						case "admin":
							{
								pinfo.badgeText = "LIEUTENANT (ADMIN)";
								break;
							}
						case "moderator":
							{
								pinfo.badgeText = "CADET (MOD)";
								break;
							}
						case "juniormod":
							{
								pinfo.badgeText = "FACILITY GUARD (JR.MOD)";
								break;
							}
					}
					ev.Player.RankName = badgetext;
				}
				if (pinfo.isTop20)
				{
					UserGroup rank = ev.Player.Group;

					if (rank == null)
					{
						ev.Player.RankName = "TOP 20 PLAYTIME";
						ev.Player.RankColor = "lime";
					}
					else
					{
						pinfo.badgeColor = rank.BadgeColor;
						Log.Info($"[TOP 20] {ev.Player.Nickname} ({ev.Player.UserId}) Is Top20, and is alternating badges. {rank.BadgeText} {rank.BadgeColor}");
					}
				}
				PlayerInfoDict.Remove(ev.Player.UserId); // Remove old shit
				PlayerInfoDict.Add(ev.Player.UserId, pinfo); // Add it back
			}

			if(!plugin.Config.IsLobby)
				Timing.RunCoroutine(MOTD(ev.Player));
			return;
		}

		public async Task BanChecks(PreAuthenticatingEventArgs ev)
		{
			Log.Info($" USERID: {ev.UserId} connected, pulling player info from web api!");
			if(plugin.Config.IsLobby)
			{
				await WebTask.IsKickedDetail(plugin.Config.APIKey, ev.UserId);
				await WebTask.IsBannedDetail(plugin.Config.APIKey, ev.UserId, false);
			}
			else
			{
				
				if (await WebTask.GetPlayerInfo(plugin.Config.APIKey, ev.UserId))
				{
					PlayerInfo pi;
					if(PlayerInfoDict.TryGetValue(ev.UserId, out pi));
					{
						if (pi.isBanned)
						{
							Player ply = Player.Get(ev.UserId);
							if (ply != null)
								SendClientToServer(ply, 7790);
							else
							{
								StartStopwatch();
								ToKick.Add(new PlayerToKick(ev.UserId, KickReason.Ban));
							}
							return;
						}
					}
				}
			}
		}

		public static void DisplayBannedInfo(string uidorip, string name, string ends, string reason)
		{
			Exiled.API.Features.Log.Info($"Displaybannedinfo {uidorip} {name} {ends} {reason}");
			

			Timing.RunCoroutine(DisplayBannedInfoCo(uidorip, ends, reason));			
		}

		public static IEnumerator<float> DisplayBannedInfoCo(string uid, string ends, string reason)
		{
			yield return Timing.WaitForSeconds(15f);
			Exiled.API.Features.Log.Info($"Player not null, setting up timing");
			Player ply = Player.Get(uid);

			ply.ClearBroadcasts();
			ply.Broadcast(150, $"<color=red><b>You are currently banned from the entire KPG Network!</b></color> \nYour ban expires {ends}. \nReason: {reason}");
			ply.ReferenceHub.hints.Show(new TextHint("<color=red>You are currently banned, and cannot join our servers!</b></color> \nAppeal At: <color=blue>https://kingsplayground.fun</color>", new HintParameter[]
			{
									new StringHintParameter("")
			}, null, 1000f));

			// Mute them, so they can't say racist shit in the hub while banned.
			ply.IsMuted = true;
		}
		public static void DisplayKickInfo(string uidorip, string reason)
		{
			Timing.RunCoroutine(DisplayKickInfoCo(uidorip, reason));
		}

		public static IEnumerator<float> DisplayKickInfoCo(string uid, string reason)
		{
			yield return Timing.WaitForSeconds(10f);
			Player ply = Player.Get(uid);

			if(reason == "nokickfound")
			{
				ply.Broadcast(30, $"<color=green>Welcome to the </color><color=red>King's Playground Main Hub</color> <color=green>{ply.Nickname}!</color> \nTo join a sever walk into the portal of your desired server!");
				ply.ReferenceHub.hints.Show(new TextHint("<color=red>If you wish to return to the hub type</color> <color=yellow>.lobby</color> <color=red>in your game console.</color>", new HintParameter[]
				{
									new StringHintParameter("")
				}, null, 30f));
			}
			else
			{
				ply.ClearBroadcasts();
				ply.Broadcast(30, $"<color=red><b>You were kicked from your current server by a staff member.</b></color> \nReason: {reason}");
			}
		}
		public static void AlternateBadgeText()
		{
			foreach (Player ply in Player.List)
			{
				ply.RemoteAdminMessage("AlternateBadgeText()");
				PlayerInfo pinfo;
				if(PlayerInfoDict.TryGetValue(ply.UserId, out pinfo))
				{
					ply.RemoteAdminMessage("Pinfodict()");
					if (pinfo.isStaff && pinfo.isTop20)
					{
						ply.RemoteAdminMessage("isstaffandtop20()");
						if (bAlternate)
						{
							ply.RemoteAdminMessage("balternate()");
							ply.RankName = pinfo.badgeText;
							ply.RankColor = pinfo.badgeColor;
						}
						else
						{
							ply.RemoteAdminMessage("TOP20 SET NOW.......()");
							ply.RankName = "TOP 20 PLAYTIME";
							ply.RankColor = "lime";
						}
					}
				}
			}

			if (bAlternate)
				bAlternate = false;
			else
				bAlternate = true;
			Timing.CallDelayed(5f, AlternateBadgeText);
		}

		public IEnumerator<float> MOTD(Player hub)
		{
			yield return Timing.WaitForSeconds(6f);
			hub.Broadcast(6, $"<color=green>Welcome to </color><color=red>King's Playground </color><color=green>{hub.Nickname}!</color>");
			yield return Timing.WaitForSeconds(6f);
			hub.Broadcast(6, $"<color=yellow>Please take a minute to read our rules before playing!</color>");
			yield return Timing.WaitForSeconds(6f);
			hub.Broadcast(10, $"<color=red><b>NOTICE:</b></color> <color=white>We do NOT allow K.O.S. (Kill On Sight) on this server! </color><color=red><b>You cannot kill Class-D without valid reasoning.</b></color>");
			yield return Timing.WaitForSeconds(10f);
			hub.Broadcast(6, $"You can visit our website at <color=blue>https://kingsplayground.fun</color><color=white> for up-to-date rules and stats!</color>");
			yield return Timing.WaitForSeconds(6f);

			PlayerInfo pinfo;
			if(PlayerInfoDict.TryGetValue(hub.UserId, out pinfo))
			{
				if (pinfo.isTop20)
				{
					hub.Broadcast(10, $"You have been assigned a <color=yellow><b>TOP 20 PLAYTIME</b></color> <color=white>badge for being in the top 20 total playtime for this week!</color> <color=yellow><b>CONGRATS!</b></color>");
					yield return Timing.WaitForSeconds(10f);
				}
				if (pinfo.isStaff)
				{
					hub.Broadcast(6, $"<i>Your administrative rank has been assigned. Thanks for being part of the staff team!</i>");
					yield return Timing.WaitForSeconds(6f);
				}
			}
		}

		public async Task DisconnectChecks(LeftEventArgs ev)
		{
			PlayerInfo pinfo;
			if(PlayerInfoDict.TryGetValue(ev.Player.UserId, out pinfo))
			{
				long sessionTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - pinfo.connectTime;
				Log.Info($"[Player Update] Logging Playerdata for {ev.Player} ({ev.Player.UserId})[{ev.Player.IPAddress}] Session Time: {sessionTime} DNT: {ev.Player.ReferenceHub.serverRoles.DoNotTrack}");
				await WebTask.UpdatePlayer(plugin.Config.APIKey, plugin.Config.ServerSlug, ev.Player.UserId, ev.Player.IPAddress, ev.Player.Nickname, ev.Player.ReferenceHub.serverRoles.DoNotTrack, sessionTime);
			}

			if (PlayerInfoDict.ContainsKey(ev.Player.UserId))
				PlayerInfoDict.Remove(ev.Player.UserId);
		}
		public async Task<bool> DoBan(BanningEventArgs ev)
		{
			string banned_user_id = ev.Target.UserId;
			string banned_nickname = ev.Target.Nickname;
			string banned_ip_address = ev.Target.IPAddress;
			int banduration = ev.Duration;
			Player adminHub = ev.Issuer;

			Log.Info("--------------------------------------");
			Log.Info("Ban detected, plugin taking over:");
			Log.Info($"Banned Name: {banned_nickname}");
			Log.Info($"Banned ID: {banned_user_id}");
			Log.Info($"Banned IP: {banned_ip_address}");
			Log.Info($"Admin Name: {adminHub.Nickname}");
			Log.Info($"Admin ID: {adminHub.UserId}");
			Log.Info($"Duration: {banduration}");
			if (banduration.ToString().Contains("1576800000"))
			{
				banduration = 0;
				Log.Info($"Duration: UPDATED TO PERM!");
			}
			string reason = ev.Reason;
			if(reason == "")
			{
				reason = "No reason provided. Please contact a Head Administrator for further details.";
			}
			if (await WebTask.IssueBan(plugin.Config.APIKey, banned_user_id, banned_nickname, banned_ip_address, adminHub.UserId, banduration, reason, ev.Target.ReferenceHub.serverRoles.DoNotTrack))
			{
				Log.Info($"Successfully pushed ban for {banned_user_id} ({banned_ip_address}) to the web API!");
				Log.Info("--------------------------------------");
				SendClientToServer(ev.Target, 7790);
				// We can safely remove the ban since the web client will handle it from here.
				//BanHandler.RemoveBan(ev.Target.UserId, ev.);
				return true;
			}
			else
			{
				// Error out to requesting admin
				adminHub.Broadcast(15, $"ERROR while adding ban to web API for: {banned_nickname}({banned_user_id})");
				Log.Error($"FATAL BANNING ERROR! WebTask.IssueBan() Failed to push to web API");
				

				// Actually ban them since the webapi decided to do the funny...
				BanDetails ban = new BanDetails
				{
					OriginalName = ev.Target.Nickname,
					Id = ev.Target.UserId,
					IssuanceTime = TimeBehaviour.CurrentTimestamp(),
					Expires = DateTime.UtcNow.AddMinutes((double)ev.Duration).Ticks,
					Reason = ev.Reason,
					Issuer = ev.Issuer.UserId
				};
				BanHandler.IssueBan(ban, BanHandler.BanType.UserId);
				Log.Info("Pushed manual server-side ban.");
				Log.Info("--------------------------------------");
				return false;
			}
			return false;
		}
		public async Task<bool> DoKick(KickingEventArgs ev)
		{
			string banned_user_id = ev.Target.UserId;
			Player adminHub = ev.Issuer;
			string adminId = "idk";

			Log.Info("Kick DETECTED:");
			Log.Info($"Banned ID: {banned_user_id}");
			Log.Info($"Admin Name: {adminHub.Nickname}");
			Log.Info($"Admin ID: {adminId}");

			if (await WebTask.IssueKick(plugin.Config.APIKey, banned_user_id, ev.Target.Nickname, adminId, ev.Reason))
			{
				Log.Info($"Successfully pushed kick for {banned_user_id} to the web API!");
				SendClientToServer(ev.Target, 7796);
				// We can safely remove the ban since the web client will handle it from here.
				//BanHandler.RemoveBan(ev.Details.Id, ev.Type);
				return true;
			}
			else
			{
				// Error out to requesting admin
				adminHub.Broadcast(15, $"ERROR while adding kick to web API for: {ev.Target.Nickname}({banned_user_id})");
				Log.Error($"FATAL KICKING ERROR! WebTask.IssueKick() Failed to push to web API");
				ServerConsole.Disconnect(ev.Target.GameObject.gameObject, ev.Reason);

				return false;
			}

		}
		public static bool isDeveloper(Player hub)
		{
			if (hub.UserId == "thomasjosif@northwood")
			{
				return true;
			}
			return false;
		}

		public static void TryReplacePlayer(Player replacing)
		{
			Log.Info($"[Tryreplace] Tryreplace now on {replacing} currently {replacing.Role}");

			if (replacing.Role != RoleType.Spectator && replacing.Role != RoleType.None && replacing.Role != RoleType.Tutorial)
			{
				Log.Info($"[Tryreplace] ATTEMPTING REPLACE ON {replacing.Nickname}");
				// Credit: DCReplace :) -- Honestly though, this was really changed from DCReplace into a better check system.

				Inventory.SyncListItemInfo items = replacing.Inventory.items;
				RoleType role = replacing.Role;
				Vector3 pos = replacing.Position;
				float health = replacing.Health;

				// New strange ammo system because the old one was fucked.
				Dictionary<Exiled.API.Enums.AmmoType, uint> ammo = new Dictionary<Exiled.API.Enums.AmmoType, uint>();
				foreach (Exiled.API.Enums.AmmoType atype in (Exiled.API.Enums.AmmoType[])Enum.GetValues(typeof(Exiled.API.Enums.AmmoType)))
				{
					ammo.Add(atype, replacing.GetAmmo(atype));
				}

				bool isScp079 = (replacing.Role == RoleType.Scp079);
				// Stuff for 079
				byte Level079 = 0;
				float Exp079 = 0f, AP079 = 0f;
				if (isScp079)
				{
					Level079 = replacing.Level;
					Exp079 = replacing.Experience;
					AP079 = replacing.Energy;
				}

				for (; ; )
				{
					Player player = Player.List.FirstOrDefault(x => x.Role == RoleType.Spectator && x.UserId != string.Empty && !x.IsOverwatchEnabled && x != replacing);
					if (player.Role == RoleType.Spectator && player.UserId != string.Empty && !player.IsOverwatchEnabled && player != replacing)
					{
						Log.Info($"[TryReplace] Replacing player {replacing.Nickname} with player {player.Nickname}");
						player.SetRole(role);
						Timing.CallDelayed(0.3f, () =>
						{
							player.Position = pos;
							player.Inventory.Clear();
							foreach (var item in items) player.Inventory.AddNewItem(item.id);
							player.Health = health;

							foreach (Exiled.API.Enums.AmmoType atype in (Exiled.API.Enums.AmmoType[])Enum.GetValues(typeof(Exiled.API.Enums.AmmoType)))
							{
								uint amount;
								if (ammo.TryGetValue(atype, out amount))
								{
									player.SetAmmo(atype, amount);
								}
								else
									Log.Error($"[KingsSCPSL] ERROR: Tried to get a value from dict that did not exist! (Ammo)");
							}
							if (isScp079)
							{
								player.Level = Level079;
								player.Experience = Exp079;
								player.Energy = AP079;
							}
							player.Broadcast(10, $"{MSG_PREFIX} You have replaced a player who has disconnected.");
						});
					}
				}
			}
		}

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
		
		public static void OnRoundEnd()
		{
			ToKick.Clear();
			stopwatch.Reset();
			cleanupStopwatch.Reset();
			_ = DoUploadLogs();
		}

		public static async Task<bool> DoUploadLogs()
		{
			await WebTask.UploadRoundLogs(logList);
			return true;
		}
		public void SendClientToServer(Player hub, ushort port)
		{
			var serverPS = PlayerManager.localPlayer.GetComponent<PlayerStats>();
			NetworkWriter writer = NetworkWriterPool.GetWriter();
			writer.WriteSingle(1f);
			writer.WriteUInt16(port);
			RpcMessage msg = new RpcMessage
			{
				netId = serverPS.netId,
				componentIndex = serverPS.ComponentIndex,
				functionHash = GetMethodHash(typeof(PlayerStats), "RpcRoundrestartRedirect"),
				payload = writer.ToArraySegment()
			};
			hub.Connection.Send<RpcMessage>(msg, 0);
			NetworkWriterPool.Recycle(writer);
		}
		private static int GetMethodHash(Type invokeClass, string methodName)
		{
			return invokeClass.FullName.GetStableHashCode() * 503 + methodName.GetStableHashCode();
		}
	}

}