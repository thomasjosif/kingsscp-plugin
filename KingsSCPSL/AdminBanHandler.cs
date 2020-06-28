using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Web;
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
using LiteNetLib;
using LiteNetLib4Mirror;
using LiteNetLib.Utils;
namespace KingsSCPSL
{
    public class PlayerManagement
    {
        public static async Task<bool> IssueBan(string userID, string userName, string adminID, string durationinseconds, BanHandler.BanType type, string reason)
        {
            using (HttpClient client = new HttpClient())
            {
                string typeofban = "-1";
                if (type == BanHandler.BanType.UserId)
                {
                    typeofban = "0";
                }
                else if (type == BanHandler.BanType.IP)
                {
                    typeofban = "1";
                    return true;
                }
                if (reason != "")
                    reason = WebUtility.UrlEncode(reason);
                else
                    reason = WebUtility.UrlEncode("No reason provided.");

                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/issueban.php?KEY=" + Plugin.APIKey + "&STEAMID=" + userID + "&USERNAME=" + userName + "&AID=" + adminID + "&TYPE=" + typeofban + "&DURATION=" + durationinseconds + "&REASON=" + reason);

                if (!webRequest.IsSuccessStatusCode)
                {
                    Log.Error("Web API connection error in IssueBan(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                if (apiResponse.Contains("OK"))
                    return true;
                else
                    return false;
            }
        }

        public static async Task<bool> IsBanned(string userID, bool ipcheck)
        {
            using (HttpClient client = new HttpClient())
            {
                string ipcheckstatus = "UID";
                if (ipcheck)
                {
                    ipcheckstatus = "IP";
                }
                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/bancheck.php?KEY=" + Plugin.APIKey + "&STEAMID=" + userID + "&TYPE=" + ipcheckstatus);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Log.Error("Web API connection error in IsBanned(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();
                Log.Info($"BAN API RESPONSE: {apiResponse}");
                if (apiResponse.Contains("OK"))
                    return false;
                else if (apiResponse.Contains("BAN"))
                    return true;
                else
                    return false;
            }
        }

        public static async Task<string> GetAdminRole(string userID)
        {
            using (HttpClient client = new HttpClient())
            {
                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/admincheck.php?KEY=" + Plugin.APIKey + "&STEAMID=" + userID);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Log.Error("Web API connection error in GetAdminRole(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return "";
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                return apiResponse;
            }
        }

        public static async Task<string> GetAdminID(string userID)
        {
            using (HttpClient client = new HttpClient())
            {
                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/adminidcheck.php?KEY=" + Plugin.APIKey + "&STEAMID=" + userID);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Log.Error("Web API connection error in GetAdminRole(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return "";
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                return apiResponse;
            }
        }

        public static async Task<bool> UpdatePlaytime(string userID, long connecttime, long disconnecttime, int serverport)
        {
            using (HttpClient client = new HttpClient())
            {
                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/playtime.php?KEY=" + Plugin.APIKey + "&STEAMID=" + userID + "&CONNECT=" + connecttime + "&DISCONNECT=" + disconnecttime + "&PORT=" + serverport);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Log.Error("Web API connection error in UpdatePlaytime(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                Log.Debug($"[Playtime] The player API returned: {apiResponse}");
                return true;
            }
        }
    }
}