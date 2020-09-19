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
using GameCore;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.Events.EventArgs;
using Exiled.Permissions.Extensions;
using Newtonsoft.Json.Linq;
using static KingsSCPSL.PlayerEvents;

namespace KingsSCPSL
{
    public class WebTask
    {
        public static async Task<bool> GetPlayerInfo(string APIKey, string userID)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + APIKey);
                using (HttpResponseMessage webRequest = await client.GetAsync("https://api.kingsplayground.fun/v1/player/" + userID))
                {

                    if (!webRequest.IsSuccessStatusCode)
                    {
                        Exiled.API.Features.Log.Error("Web API connection error in GetPlayerInfo(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                        return false;
                    }

                    string apiResponse = await webRequest.Content.ReadAsStringAsync();
                    Exiled.API.Features.Log.Info($"[GetPlayerInfo] ({userID}) The API Returned: {apiResponse}");

                    JObject json = JObject.Parse(apiResponse);
                    if (json.ContainsKey("error"))
                    {
                        PlayerInfo pinfo;
                        pinfo.needsCreating = true;
                        pinfo.connectTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        pinfo.isStaff = false;
                        pinfo.isBanned = false;
                        pinfo.isTop20 = false;
                        pinfo.badgeColor = "";
                        pinfo.badgeText = "";
                        pinfo.adminRank = "none";

                        PlayerInfoDict.Add(userID, pinfo);
                    }
                    else
                    {
                        PlayerInfo pinfo;
                        pinfo.needsCreating = false;
                        pinfo.connectTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                        string rank = json.Value<string>("admin_rank");
                        pinfo.isStaff = (rank != "none");
                        pinfo.isTop20 = json.Value<bool>("top20");
                        pinfo.isBanned = json.Value<bool>("banned");
                        pinfo.badgeColor = "";
                        pinfo.badgeText = "";
                        pinfo.adminRank = rank;

                        PlayerInfoDict.Add(userID, pinfo);
                    }
                    return true;
                }
            }
        }
        public static async Task<bool> CreatePlayer(string APIKey, string userId, string name, bool dnt)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + APIKey);

                JObject ply = new JObject
                {
                    { "steamid", new JValue(userId) },
                    { "name", new JValue(name) },
                    { "do_not_track", new JValue(Convert.ToInt32(dnt)) }
                };

                var httpContent = new StringContent(ply.ToString());
                httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var webRequest = await client.PostAsync("https://api.kingsplayground.fun/v1/player", httpContent);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Exiled.API.Features.Log.Error("Web API connection error in GetPlayerInfo(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                Exiled.API.Features.Log.Debug($"[CreatePlayer] The player API returned: {apiResponse}");
                JObject json = JObject.Parse(apiResponse);
                if (json.ContainsKey("error"))
                    return false;
                return true;
            }
        }
        public static async Task<bool> IssueBan(string APIKey, string userID, string userName, string ipAddr, string adminID, int durationinseconds, string reason, bool dnt)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + APIKey);

                JObject ban = new JObject
                {
                    { "ip", new JValue(ipAddr) },
                    { "steamid", new JValue(userID) },
                    { "name", new JValue(userName) },
                    { "duration", new JValue(durationinseconds) },
                    { "aid", new JValue(adminID) },
                    { "reason", new JValue(reason) },
                    { "do_not_track", new JValue(Convert.ToInt32(dnt)) }
                };

                var httpContent = new StringContent(ban.ToString());
                httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var webRequest = await client.PostAsync("https://api.kingsplayground.fun/v1/player/ban", httpContent);

                if (!webRequest.IsSuccessStatusCode)
                {
                    Exiled.API.Features.Log.Error("Web API connection error in IssueBan(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                Exiled.API.Features.Log.Debug($"[IssueBan] The player API returned: {apiResponse}");
                JObject json = JObject.Parse(apiResponse);
                if (json.ContainsKey("error"))
                    return false;
                return true;
            }
        }
        public static async Task<bool> IssueKick(string APIKey, string userID, string userName, string adminID, string reason)
        {
            using (HttpClient client = new HttpClient())
            {
                if (reason != "")
                    reason = WebUtility.UrlEncode(reason);
                else
                    reason = WebUtility.UrlEncode("No reason provided.");

                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/issuekick.php?KEY=" + APIKey + "&STEAMID=" + userID + "&USERNAME=" + userName + "&AID=" + adminID + "&REASON=" + reason);

                if (!webRequest.IsSuccessStatusCode)
                {
                    Exiled.API.Features.Log.Error("Web API connection error in IssueKick(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                if (apiResponse.Contains("OK"))
                    return true;
                else
                    return false;
            }
        }
        public static async Task IsBannedDetail(string APIKey, string userID, bool ipcheck)
        {
            using (HttpClient client = new HttpClient())
            {
                string ipcheckstatus = "UID";
                if (ipcheck)
                {
                    ipcheckstatus = "IP";
                }
                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/bancheckdetail.php?KEY=" + APIKey + "&STEAMID=" + userID + "&TYPE=" + ipcheckstatus);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Exiled.API.Features.Log.Error("Web API connection error in IsBannedDetail(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();
                Exiled.API.Features.Log.Info($"BAN API RESPONSE: {apiResponse}");

                JObject json = JObject.Parse(apiResponse);

                bool banned = json.Value<bool>("banned");
                if(banned)
                {
                    if (!ipcheck)
                    {
                        string userid = json.Value<string>("authid");
                        string name = json.Value<string>("name");
                        string ends = json.Value<string>("ends");
                        string reason = json.Value<string>("reason");
                        PlayerEvents.DisplayBannedInfo(userid, name, ends, reason);
                    }
                }
            }
        }
        public static async Task IsKickedDetail(string APIKey, string userID)
        {

            using (HttpClient client = new HttpClient())
            {
                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/recentkickdetail.php?KEY=" + APIKey + "&STEAMID=" + userID);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Exiled.API.Features.Log.Error("Web API connection error in IsKickeddetail(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();
                Exiled.API.Features.Log.Info($"BAN API RESPONSE FOR {userID}: {apiResponse}");

                JObject json = JObject.Parse(apiResponse);

                bool banned = json.Value<bool>("recentkick");
                if (banned)
                {
                    string reason = json.Value<string>("reason");
                    PlayerEvents.DisplayKickInfo(userID, reason);
                }
                else
                {
                    PlayerEvents.DisplayKickInfo(userID, "nokickfound");
                }
            }
        }
        public static async Task<bool> UpdatePlayer(string APIKey, string slug, string userID, string ipaddr, string nickname, bool dnt, long connecttime)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + APIKey);

                JObject ply = new JObject
                {
                    { "ip", new JValue(ipaddr) },
                    { "name", new JValue(nickname) },
                    { "server_slug", new JValue(slug) },
                    { "session_time", new JValue(connecttime) },
                    { "do_not_track", new JValue(Convert.ToInt32(dnt)) }
                };

                var httpContent = new StringContent(ply.ToString());
                httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var webRequest = await client.PutAsync("https://api.kingsplayground.fun/v1/player/" + userID, httpContent);
                if (!webRequest.IsSuccessStatusCode)
                {
                    Exiled.API.Features.Log.Error("Web API connection error in UpdatePlaytime(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                Exiled.API.Features.Log.Debug($"[UpdatePlayer] The player API returned: {apiResponse}");
                JObject json = JObject.Parse(apiResponse);
                if (json.ContainsKey("error"))
                    return false;
                return true;
            }
        }
        public static async Task<bool> UploadRoundLogs(List<PlayerEvents.CommandLog> loglist)
        {
            using (HttpClient client = new HttpClient())
            {
                var webRequest = await client.GetAsync("https://bans.kingsplayground.fun/playtime.php?KEY=");
                if (!webRequest.IsSuccessStatusCode)
                {
                    Exiled.API.Features.Log.Error("Web API connection error in UpdatePlaytime(): " + webRequest.StatusCode + " - " + webRequest.Content.ReadAsStringAsync());
                    return false;
                }

                string apiResponse = await webRequest.Content.ReadAsStringAsync();

                Exiled.API.Features.Log.Debug($"[Playtime] The player API returned: {apiResponse}");
                return true;
            }
        }
    }
}