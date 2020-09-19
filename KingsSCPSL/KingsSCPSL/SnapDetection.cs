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
using LiteNetLib;
using LiteNetLib4Mirror;
using LiteNetLib.Utils;
using System.Diagnostics;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.Events.EventArgs;
using Exiled.Permissions.Extensions;

namespace KingsSCPSL
{
    class SnapDetection
    {
		/***************************************************************************************************
		 ************************************* SERVER SNAP DETECTION ***************************************
		 ***************************************************************************************************/
		private static int iSCPCount = 0;
		private static int iFacilityGuardCount = 0;
		private static int iClassDCount = 0;
		private static int iScientistCount = 0;
		private static int iTotalPlayers = 0;
		private static int iNotSpawnedCount = 0;
		private static int iTotalChaos = 0;
		private static bool bChaosOnStart = false;
		private static int iMaxPlayers = 64;

		private bool bSpectatorsMuted = false;

		private static string sRespawnQueue = "4014314031441404134041431430144144134414314031441441331441440131";
		static char[] sRespawnQueueArray;

		public static List<string> hubNotSpawnedList = new List<string>();


		public static void ResetVarsAndCheckSnap()
		{
			sRespawnQueueArray = sRespawnQueue.ToCharArray();
			iClassDCount = 0;
			iScientistCount = 0;
			iFacilityGuardCount = 0;
			iSCPCount = 0;
			iNotSpawnedCount = 0;
			iTotalChaos = 0;
			bChaosOnStart = false;
			hubNotSpawnedList.Clear();

			foreach (Player hub in Player.List)
			{
				// Since this event fires before everyone has initially spawned, you need to wait before doing things like changing their health, adding items, etc
				Timing.RunCoroutine(CountPlayer(hub));
			}
			Timing.RunCoroutine(CheckForSnap());
		}

		private static IEnumerator<float> CountPlayer(Player hub)
		{
			// Wait 4 seconds to make sure everyone is spawned in correctly
			yield return Timing.WaitForSeconds(4f);

			if (hub != null)
			{
				// Count spawned players and add to global variables.
				switch (hub.Role)
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
						if (!hub.IsOverwatchEnabled)
						{
							// Player was not spawned (or latejoined) and will need to be spawned manually
							iTotalPlayers++;
							iNotSpawnedCount++;
							hubNotSpawnedList.Add(hub.UserId);
						}
						break;
				}
			}
		}

		public static IEnumerator<float> CheckForSnap()
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
					Player playertospawn;

					// Loop over max clients till we find a replacement
					for (int j = 0; j < iMaxPlayers; j++)
					{
						index = random.Next(hubNotSpawnedList.Count());
						playertospawn = Player.Get(hubNotSpawnedList[index]);
						hubNotSpawnedList.Remove(hubNotSpawnedList[index]);
						if (playertospawn.Role == RoleType.Spectator && playertospawn != null && !playertospawn.IsOverwatchEnabled)
						{

							Log.Info($"Spawning in valid player {playertospawn.Nickname} as {Enum.GetName(typeof(RoleType), roletospawn)}");

							playertospawn.SetRole(roletospawn);
							playertospawn.Broadcast(10, $"{PlayerEvents.MSG_PREFIX} Since you didn't spawn naturally you were put in as a {Enum.GetName(typeof(RoleType), roletospawn)}");
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
			int i049Count = 0;
			int i079Count = 0;
			int i106Count = 0;
			int i173Count = 0;
			int i096Count = 0;
			int i93953Count = 0;
			int i93989Count = 0;

			foreach (Player rh in Player.List)
			{
				if (rh != null)
				{

					iTotalPlayers++;
					switch (rh.Role)
					{
						case RoleType.Scp049:
							iSCPCount++;
							i049Count++;
							Log.Info($"Found {rh.UserId} (SCP-049)");
							break;
						case RoleType.Scp079:
							iSCPCount++;
							i079Count++;
							Log.Info($"Found {rh.UserId} (SCP-079)");
							break;
						case RoleType.Scp106:
							iSCPCount++;
							i106Count++;
							Log.Info($"Found {rh.UserId} (SCP-106)");
							break;
						case RoleType.Scp173:
							iSCPCount++;
							i173Count++;
							Log.Info($"Found {rh.UserId} (SCP-173)");
							break;
						case RoleType.Scp096:
							iSCPCount++;
							i096Count++;
							Log.Info($"Found {rh.UserId} (SCP-096)");
							break;
						case RoleType.Scp93953:
							iSCPCount++;
							i93953Count++;
							Log.Info($"Found {rh.UserId} (SCP-939-53)");
							break;
						case RoleType.Scp93989:
							iSCPCount++;
							i93989Count++;
							Log.Info($"Found {rh.UserId} (SCP-939-89)");
							break;
						default:
							Log.Info($"Found {rh.UserId} (Not SCP)");
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
				for (int i = 0; i < (iNeededSCPCount - iSCPCount); i++)
				{
					Player playertospawn;
					RoleType roletospawn = RoleType.None;
					for (int j = 0; j < iMaxPlayers; j++)
					{
						playertospawn = GetRandomPlayer();
						if (playertospawn != null && !playertospawn.IsOverwatchEnabled && NotSCP(playertospawn))
						{
							if (i096Count == 0)
							{
								i096Count++;
								roletospawn = RoleType.Scp096;
							}
							else if (i079Count == 0)
							{
								i079Count++;
								roletospawn = RoleType.Scp079;
							}
							else if (i173Count == 0)
							{
								i173Count++;
								roletospawn = RoleType.Scp173;
							}
							else if (i106Count == 0)
							{
								i106Count++;
								roletospawn = RoleType.Scp106;
							}
							else if (i049Count == 0)
							{
								i049Count++;
								roletospawn = RoleType.Scp049;
							}
							else if (i93953Count == 0)
							{
								i93953Count++;
								roletospawn = RoleType.Scp93953;
							}
							else if (i93989Count == 0)
							{
								i93989Count++;
								roletospawn = RoleType.Scp93989;
							}
							else
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
							}
							Log.Info($"Spawning in valid player {playertospawn.Nickname} as {Enum.GetName(typeof(RoleType), roletospawn)} to fix the SCP Count!");

							playertospawn.SetRole(roletospawn);
							playertospawn.Broadcast(10, $"{PlayerEvents.MSG_PREFIX} Since not enough SCP's spawned your were put in as a {Enum.GetName(typeof(RoleType), roletospawn)}");
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

		static char[] RemoveFromArrayAt(char[] source, int removeAt)
		{
			if (source == null || removeAt > source.Length)
				return null;

			char[] result = new char[source.Length - 1];
			Array.Copy(source, result, removeAt);
			Array.Copy(source, removeAt + 1, result, removeAt, source.Length - removeAt - 1);

			return result;
		}
		public static bool NotSCP(Player player)
		{
			switch (player.Role)
			{
				case RoleType.Scp049:
				case RoleType.Scp0492:
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
		public static Player GetRandomPlayer()
		{
			var random = new System.Random();
			return Player.Get(PlayerManager.players[random.Next(PlayerManager.players.Count)]);
		}
	}
}
