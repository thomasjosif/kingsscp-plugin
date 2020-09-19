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
	public class ServerEvents
	{
		public MainClass plugin;
		public ServerEvents(MainClass plugin)
		{
			this.plugin = plugin;
		}

		public void OnRoundStart()
		{
			SnapDetection.ResetVarsAndCheckSnap();
		}

		public void OnTeamRespawn(RespawningTeamEventArgs ev)
		{
		}

		public void OnRoundEnd(RoundEndedEventArgs ev)
		{
			PlayerEvents.OnRoundEnd();
		}

		public void CheckRoundEnd(EndingRoundEventArgs ev)
		{
			/*	WIP
			 *	int iCheckChaos = 0;
				int iCheckClassD = 0;
				int iCheckSCP = 0;
				int iCheckSci = 0;
				int iCheckMTFGuard = 0;

				if (ev.Allow)
				{
					foreach (ReferenceHub hub in Player.GetHubs())
					{
						if (hub != null)
						{
							// Count spawned players and add to global variables.
							switch (hub.characterClassManager.CurClass)
							{
								case RoleType.ChaosInsurgency:
									iCheckChaos++;
									break;
								case RoleType.ClassD:
									iCheckClassD++;
									break;
								case RoleType.Scientist:
									iCheckSci++;
									break;
								case RoleType.Scp049:
								case RoleType.Scp0492:
								case RoleType.Scp079:
								case RoleType.Scp106:
								case RoleType.Scp173:
								case RoleType.Scp096:
								case RoleType.Scp93953:
								case RoleType.Scp93989:
									iCheckSCP++;
									break;
								case RoleType.FacilityGuard:
								case RoleType.NtfCadet:
								case RoleType.NtfLieutenant:
								case RoleType.NtfCommander:
								case RoleType.NtfScientist:
									iCheckMTFGuard++;
									break;
							}
						}
					}
				}*/
		}

		public void WaitingForPlayers()
		{
			Timing.CallDelayed(5f, PlayerEvents.AlternateBadgeText);
		}
	}
}