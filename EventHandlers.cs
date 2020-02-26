using System.Collections.Generic;
using EXILED;
using MEC;
using EXILED.Extensions;
using UnityEngine;

namespace KingsSCPSL
{
	public class EventHandlers
	{
		public Plugin plugin;
		public EventHandlers(Plugin plugin) => this.plugin = plugin;

		private int iTotal = 0;
		private int iNotSpawnedCount = 0;
		private List<int> hubNotSpawnedList = new List<int>();

		public void OnRoundStart()
		{
			iTotal = 0;
			iNotSpawnedCount = 0;
			hubNotSpawnedList.Clear();

			foreach (ReferenceHub hub in Player.GetHubs())
			{
				// Since this event fires before everyone has initially spawned, you need to wait before doing things like changing their health, adding items, etc
				Timing.RunCoroutine(CountSpawnedPlayers(hub));
			}

			Timing.RunCoroutine(CheckForSnap());
		}

		public IEnumerator<float> CountSpawnedPlayers(ReferenceHub hub)
		{
			// Wait 4 seconds to make sure everyone is spawned in correctly
			yield return Timing.WaitForSeconds(4f);

			if (hub.characterClassManager.CurClass == RoleType.Spectator)
			{
				iTotal++;
				iNotSpawnedCount++;
				hubNotSpawnedList.Add(hub.GetInstanceID());
			}
			else
			{
				if (hub != null)
				{
					iTotal++;
				}
			}

		}

		public IEnumerator<float> CheckForSnap()
		{
			//Wait 8 seconds to make sure everyone is counted
			yield return Timing.WaitForSeconds(8f);


			double percent = iNotSpawnedCount / iTotal;

			yield return Timing.WaitForSeconds(5f);
			if (percent >= 0.35)
			{
				foreach (GameObject o in PlayerManager.players)
				{
					ReferenceHub rh = o.GetComponent<ReferenceHub>();
					rh.Broadcast(10, "Round restart in 3 seconds since approximately 40% of players did not spawn correctly!");
				}
				yield return Timing.WaitForSeconds(3f);
				PlayerManager.localPlayer.GetComponent<PlayerStats>()?.Roundrestart();
			}
			else
			{
				foreach (GameObject o in PlayerManager.players)
				{
					foreach (int id in hubNotSpawnedList)
					{
						ReferenceHub rh = o.GetComponent<ReferenceHub>();
						if (id == rh.GetInstanceID())
						{
							if (rh != null)
							{
								rh.Broadcast(10, "Since you didn't spawn natrually you were put in as a ClassD.");
								rh.characterClassManager.SetClassID(RoleType.ClassD);
							}
						}
					}
				}
			}
		}
		public void OnPlayerDeath(ref PlayerDeathEvent ev)
		{
			/*if (ev.Killer != null && ev.Player != null)
			{
				foreach (GameObject o in PlayerManager.players)
				{
					ReferenceHub rh = o.GetComponent<ReferenceHub>();
					if (rh.serverRoles.RemoteAdmin)
						rh.queryProcessor.TargetReply(rh.characterClassManager.connectionToClient, $"KingsSCPSL#{ev.Info.Attacker} killed {ev.Player.nicknameSync.MyNick} - {ev.Player.characterClassManager.UserId} ({ev.Player.characterClassManager.CurClass}) with {ev.Info.Tool}.", true, true, string.Empty);

				}
			}*/
		}
	}
}