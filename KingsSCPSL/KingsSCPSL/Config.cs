using System;
using System.Collections.Generic;
using System.IO;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.API.Interfaces;
using Exiled.Events;
using Handlers = Exiled.Events.Handlers;

namespace KingsSCPSL
{
    using Exiled.API.Enums;
    using Exiled.API.Features;

    public sealed class Config : IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool IsLobby { get; set; } = false;
        public string APIKey { get; private set; } = "setapikeyhere";
        public string ServerSlug { get; private set; } = "setslughere";
    }
}