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

using System;
using System.Collections.Generic;
using System.IO;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.API.Interfaces;
using Exiled.Events;
using Handlers = Exiled.Events.Handlers;
using MEC;

namespace KingsSCPSL
{
    using Exiled.API.Enums;
    using Exiled.API.Features;

    public class MainClass : Plugin<Config>
    {
        public override string Author { get; } = "Thomasjosif";
        public override string Name { get; } = "Kings Playground Core";
        public override string Prefix { get; } = "KingsSCPSL";
        public override Version Version { get; } = new Version(3, 0, 0);
        public override Version RequiredExiledVersion { get; } = new Version(2, 0, 0);

        public PlayerEvents PlayerEvents;
        public ServerEvents ServerEvents;

        public override PluginPriority Priority { get; } = PluginPriority.Medium;
        public List<CoroutineHandle> Coroutines = new List<CoroutineHandle>();

        public override void OnEnabled()
		{
            base.OnEnabled();
            try
			{
				Log.Debug("Initializing event handlers for King's SCPSL....");

                //Set instance varible to a new instance, this should be nulled again in OnDisable
                PlayerEvents = new PlayerEvents(this);
                ServerEvents = new ServerEvents(this);

                //Hook the events you will be using in the plugin. You should hook all events you will be using here, all events should be unhooked in OnDisabled 
                Handlers.Player.Joined += PlayerEvents.OnPlayerConnect;
                Handlers.Player.Left += PlayerEvents.OnPlayerDisconnect;
                Handlers.Player.Hurting += PlayerEvents.OnPlayerHurt;
                Handlers.Player.Banning += PlayerEvents.OnPreBan;
                Handlers.Player.Banned += PlayerEvents.OnPlayerBanned;
                Handlers.Player.PreAuthenticating += PlayerEvents.OnPreAuth;
                Handlers.Player.Kicking += PlayerEvents.OnPreKick;
                Handlers.Server.RoundStarted += ServerEvents.OnRoundStart;
                Handlers.Server.RoundEnded += ServerEvents.OnRoundEnd;
				Handlers.Server.WaitingForPlayers += ServerEvents.WaitingForPlayers;
				Handlers.Server.RespawningTeam += ServerEvents.OnTeamRespawn;
                Handlers.Server.SendingRemoteAdminCommand += PlayerEvents.OnCommand;
                Handlers.Server.SendingConsoleCommand += PlayerEvents.OnConsoleCommand;

                Log.Info($"KingsSCPSL plugin loaded. Written by Thomasjosif");
			}
			catch (Exception e)
			{
				//This try catch is redundant, as EXILED will throw an error before this block can, but is here as an example of how to handle exceptions/errors
				Log.Error($"There was an error loading the plugin: {e}");
			}
        }
        public override void OnDisabled()
		{
            base.OnDisabled();

            Handlers.Player.Joined -= PlayerEvents.OnPlayerConnect;
            Handlers.Player.Left -= PlayerEvents.OnPlayerDisconnect;
            Handlers.Player.Hurting -= PlayerEvents.OnPlayerHurt;
            Handlers.Player.Banned -= PlayerEvents.OnPlayerBanned;
            Handlers.Player.PreAuthenticating -= PlayerEvents.OnPreAuth;

            Handlers.Server.RoundStarted -= ServerEvents.OnRoundStart;
            Handlers.Server.RoundEnded -= ServerEvents.OnRoundEnd;
            Handlers.Server.WaitingForPlayers -= ServerEvents.WaitingForPlayers;
            Handlers.Server.RespawningTeam -= ServerEvents.OnTeamRespawn;
            Handlers.Server.SendingRemoteAdminCommand -= PlayerEvents.OnCommand;
            Handlers.Server.SendingConsoleCommand -= PlayerEvents.OnConsoleCommand;

            PlayerEvents = null;
            ServerEvents = null;
        }

	}
}