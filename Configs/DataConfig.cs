﻿using Levante.Rotations;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Levante.Util;
using Levante.Helpers;
using System.Linq;
using System.Threading.Channels;
using Serilog;

namespace Levante.Configs
{
    public class DataConfig : IConfig
    {
        // TODO: Rework this entire configuration because this one file is getting pretty large...

        public static string FilePath { get; } = @"Configs/dataConfig.json";

        [JsonProperty("DiscordIDLinks")]
        public static List<DiscordIDLink> DiscordIDLinks { get; set; } = new();

        [JsonProperty("AnnounceDailyLinks")]
        public static List<ulong> AnnounceDailyLinks { get; set; } = new();

        [JsonProperty("AnnounceWeeklyLinks")]
        public static List<ulong> AnnounceWeeklyLinks { get; set; } = new();

        [JsonProperty("AnnounceEmblemLinks")]
        public static List<EmblemAnnounceLink> AnnounceEmblemLinks { get; set; } = new();

        public class DiscordIDLink
        {
            [JsonProperty("DiscordID")]
            public ulong DiscordID { get; set; } = 0;

            [JsonProperty("BungieMembershipID")]
            public string BungieMembershipID { get; set; } = "-1";

            [JsonProperty("BungieMembershipType")]
            public string BungieMembershipType { get; set; } = "-1";

            [JsonProperty("UniqueBungieName")]
            public string UniqueBungieName { get; set; } = "Guardian#0000";

            [JsonProperty("AccessToken")]
            public string AccessToken { get; set; } = null;

            [JsonProperty("RefreshToken")]
            public string RefreshToken { get; set; } = null;

            [JsonProperty("AccessExpiration")]
            public DateTime AccessExpiration { get; set; } = DateTime.Now;

            [JsonProperty("RefreshExpiration")]
            public DateTime RefreshExpiration { get; set; } = DateTime.Now;
        }

        public class EmblemAnnounceLink
        {
            [JsonProperty("ChannelID")]
            public ulong ChannelID { get; set; } = 0;

            [JsonProperty("RoleID")]
            public ulong RoleID { get; set; } = 0;
        }

        public static string GetValidDestinyMembership(string BungieTag, out string MembershipType)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-API-Key", BotConfig.BungieApiKey);

                var response = client.GetAsync($"https://www.bungie.net/platform/Destiny2/SearchDestinyPlayer/-1/" + Uri.EscapeDataString(BungieTag)).Result;
                var content = response.Content.ReadAsStringAsync().Result;
                dynamic item = JsonConvert.DeserializeObject(content);

                if (IsBungieAPIDown(content))
                {
                    MembershipType = null;
                    return null;
                }

                if (item != null)
                    for (var i = 0; i < item.Response.Count; i++)
                    {
                        string memId = item.Response[i].membershipId;

                        var memResponse = client
                            .GetAsync(
                                $"https://www.bungie.net/Platform/Destiny2/-1/Profile/{memId}/LinkedProfiles/?getAllMemberships=true")
                            .Result;
                        var memContent = memResponse.Content.ReadAsStringAsync().Result;
                        dynamic memItem = JsonConvert.DeserializeObject(memContent);

                        var lastPlayed = new DateTime();
                        var goodProfile = -1;

                        if (memItem == null || memItem.ErrorCode != 1) continue;

                        for (var j = 0; j < memItem.Response.profiles.Count; j++)
                        {
                            if (memItem.Response.profiles[j].isCrossSavePrimary == true)
                            {
                                MembershipType = memItem.Response.profiles[j].membershipType;
                                return memId;
                            }

                            if (DateTime.Parse(memItem.Response.profiles[j].dateLastPlayed.ToString()) <= lastPlayed) continue;

                            lastPlayed = DateTime.Parse(memItem.Response.profiles[j].dateLastPlayed.ToString());
                            goodProfile = j;
                        }

                        if (goodProfile == -1) continue;

                        MembershipType = memItem.Response.profiles[goodProfile].membershipType;
                        return memItem.Response.profiles[goodProfile].membershipId;
                    }
            }
            MembershipType = null;
            return null;
        }

        public static bool IsPublicAccount(string BungieTag, int memId)
        {
            bool isPublic = false;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-API-Key", BotConfig.BungieApiKey);

                var response = client.GetAsync($"https://www.bungie.net/platform/Destiny2/SearchDestinyPlayer/{memId}/" + Uri.EscapeDataString(BungieTag)).Result;
                var content = response.Content.ReadAsStringAsync().Result;
                dynamic item = JsonConvert.DeserializeObject(content);

                isPublic = item.Response[0].isPublic;
            }
            return isPublic;
        }

        public static bool IsBungieAPIDown(string JSONContent)
        {
            dynamic item = JsonConvert.DeserializeObject(JSONContent);
            string status = item.ErrorStatus;
            return !status.Equals("Success");
        }

        public static DiscordIDLink RefreshCode(DiscordIDLink DIL)
        {
            using (var client = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "client_id", $"{BotConfig.BungieClientID}" },
                    { "client_secret", $"{BotConfig.BungieClientSecret}" },
                    { "Content-Type", "application/x-www-form-urlencoded" },
                    { "grant_type", "refresh_token" },
                    { "refresh_token", $"{DIL.RefreshToken}" }
                };
                var postContent = new FormUrlEncodedContent(values);

                var response = client.PostAsync("https://www.bungie.net/Platform/App/OAuth/Token/", postContent).Result;
                var content = response.Content.ReadAsStringAsync().Result;
                dynamic item = JsonConvert.DeserializeObject(content);

                if (item.refresh_token == null || item.access_token == null)
                {
                    Log.Information("[{Type}] Received null tokens from refresh; keep all tokens the same as before.", "Data");
                    return DIL;
                }
                DIL.RefreshToken = item.refresh_token;
                DIL.AccessToken = item.access_token;

                DIL.AccessExpiration = DateTime.Now.Add(TimeSpan.FromSeconds(double.Parse($"{item.expires_in}")));
                DIL.RefreshExpiration = DateTime.Now.Add(TimeSpan.FromSeconds(double.Parse($"{item.refresh_expires_in}")));

                return DIL;
            }
        }

        public static DiscordIDLink GetLinkedUser(ulong DiscordID)
        {
            var dil = DiscordIDLinks.Find(x => x.DiscordID == DiscordID);
            if (dil == null)
                return null;
            if (dil.AccessToken == null)
                return null;
            if (dil.AccessToken.Equals("[ACCESS TOKEN]"))
                return null;
            if (dil.DiscordID == DiscordID)
            {
                if (dil.RefreshExpiration < DateTime.Now)
                {
                    return null;
                }
                else if (dil.AccessExpiration < DateTime.Now)
                {
                    dil = RefreshCode(dil);
                }
                return dil;
            }
            return null;
        }

        public static void AddUserToConfig(ulong DiscordID, string MembershipID, string MembershipType, string BungieName, OAuthHelper.CodeResult CodeResult)
        {
            DiscordIDLink dil = new()
            {
                DiscordID = DiscordID,
                BungieMembershipID = MembershipID,
                BungieMembershipType = MembershipType,
                UniqueBungieName = BungieName,
                AccessToken = CodeResult.Access,
                RefreshToken = CodeResult.Refresh,
                AccessExpiration = DateTime.Now.Add(CodeResult.AccessExpiration),
                RefreshExpiration = DateTime.Now.Add(CodeResult.RefreshExpiration)
            };

            DiscordIDLinks.Add(dil);
            string output = JsonConvert.SerializeObject(new DataConfig(), Formatting.Indented);
            File.WriteAllText(FilePath, output);
        }

        public static void AddChannelToRotationConfig(ulong ChannelID, bool IsDaily)
        {
            if (IsDaily)
            {
                AnnounceDailyLinks.Add(ChannelID);
                string output = JsonConvert.SerializeObject(new DataConfig(), Formatting.Indented);
                File.WriteAllText(FilePath, output);
            }
            else
            {
                AnnounceWeeklyLinks.Add(ChannelID);
                string output = JsonConvert.SerializeObject(new DataConfig(), Formatting.Indented);
                File.WriteAllText(FilePath, output);
            }
        }

        public static void AddEmblemChannel(ulong ChannelID, IRole Role)
        {
            AnnounceEmblemLinks.Add(Role != null ? new EmblemAnnounceLink { ChannelID = ChannelID, RoleID = Role.Id } : new EmblemAnnounceLink { ChannelID = ChannelID, RoleID = 0 });

            string output = JsonConvert.SerializeObject(new DataConfig(), Formatting.Indented);
            File.WriteAllText(FilePath, output);
        }

        public static void DeleteUserFromConfig(ulong DiscordID)
        {
            for (int i = 0; i < DiscordIDLinks.Count; i++)
                if (DiscordIDLinks[i].DiscordID == DiscordID)
                    DiscordIDLinks.RemoveAt(i);
            string output = JsonConvert.SerializeObject(new DataConfig(), Formatting.Indented);
            File.WriteAllText(FilePath, output);
        }

        public static void DeleteChannelFromRotationConfig(ulong ChannelID, bool IsDaily)
        {
            if (IsDaily)
            {
                for (int i = 0; i < AnnounceDailyLinks.Count; i++)
                    if (AnnounceDailyLinks[i] == ChannelID)
                        AnnounceDailyLinks.RemoveAt(i);
            }
            else
            {
                for (int i = 0; i < AnnounceWeeklyLinks.Count; i++)
                    if (AnnounceWeeklyLinks[i] == ChannelID)
                        AnnounceWeeklyLinks.RemoveAt(i);
            }

            string output = JsonConvert.SerializeObject(new DataConfig(), Formatting.Indented);
            File.WriteAllText(FilePath, output);
        }

        public static void DeleteEmblemChannel(ulong ChannelID)
        {
            for (int i = 0; i < AnnounceEmblemLinks.Count; i++)
                if (AnnounceEmblemLinks[i].ChannelID == ChannelID)
                    AnnounceEmblemLinks.RemoveAt(i);

            string output = JsonConvert.SerializeObject(new DataConfig(), Formatting.Indented);
            File.WriteAllText(FilePath, output);
        }

        public static void UpdateConfig()
        {
            DataConfig obj = new();
            string output = JsonConvert.SerializeObject(obj, Formatting.Indented);
            File.WriteAllText(FilePath, output);
        }

        public static bool IsExistingLinkedUser(ulong DiscordID)
        {
            var user = DiscordIDLinks.Find(x => x.DiscordID == DiscordID);
            if (user == null)
                return false;
            
            if (user.AccessToken == null || user.AccessToken.Equals("[ACCESS TOKEN]"))
                return false;

            if (user.RefreshExpiration < DateTime.Now)
                return false;

            return true;
        }

        public static bool IsExistingLinkedChannel(ulong ChannelID, bool IsDaily)
        {
            if (IsDaily)
            {
                foreach (ulong chanId in AnnounceDailyLinks)
                    if (chanId == ChannelID)
                        return true;
                return false;
            }
            else
            {
                foreach (ulong chanId in AnnounceWeeklyLinks)
                    if (chanId == ChannelID)
                        return true;
                return false;
            }
        }

        public static bool IsExistingEmblemLinkedChannel(ulong ChannelID) => AnnounceEmblemLinks.Any(x => x.ChannelID == ChannelID);

        public static int GetAFKValues(ulong DiscordID, out int XPProgress, out int PowerBonus, out PrivacySetting FireteamPrivacy, out string CharacterId, out string ErrorStatus, out long ActivityHash)
        {
            ErrorStatus = $"ResponseError";
            try
            {
                var dil = GetLinkedUser(DiscordID);
                using (var client = new HttpClient())
                {
                    int Level = 0;
                    client.DefaultRequestHeaders.Add("X-API-Key", BotConfig.BungieApiKey);
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {dil.AccessToken}");

                    var response = client.GetAsync($"https://www.bungie.net/Platform/Destiny2/" + dil.BungieMembershipType + "/Profile/" + dil.BungieMembershipID + "/?components=100,104,202,204,1000").Result;
                    var content = response.Content.ReadAsStringAsync().Result;
                    dynamic item = JsonConvert.DeserializeObject(content);

                    ErrorStatus = $"{item.ErrorStatus}";
                    if (!ErrorStatus.Equals("Success"))
                    {
                        XPProgress = -1;
                        PowerBonus = -1;
                        FireteamPrivacy = PrivacySetting.Open;
                        CharacterId = null;
                        ActivityHash = 0;
                        return -1;
                    }

                    if (item.Response.profileTransitoryData.data == null)
                    {
                        ErrorStatus = $"PlayerNotOnline";
                        XPProgress = -1;
                        PowerBonus = -1;
                        FireteamPrivacy = PrivacySetting.Open;
                        CharacterId = null;
                        ActivityHash = 0;
                        return -1;
                    }

                    CharacterId = "";
                    ActivityHash = 0;
                    DateTime mostRecentDate = new();
                    for (int i = 0; i < item.Response.profile.data.characterIds.Count; i++)
                    {
                        string charId = item.Response.profile.data.characterIds[i];
                        var activityTime = DateTime.Parse($"{item.Response.characterActivities.data[$"{charId}"].dateActivityStarted}");
                        //ulong activityHash = item.Response.characterActivities.data[$"{charId}"].currentActivityHash;
                        if (activityTime > mostRecentDate)
                        {
                            mostRecentDate = activityTime;
                            CharacterId = $"{charId}";
                            ActivityHash = item.Response.characterActivities.data[$"{charId}"].currentActivityHash;
                        }
                    }

                    FireteamPrivacy = item.Response.profileTransitoryData.data.joinability.privacySetting;

                    //first 100 levels: 4095505052 (S15); 2069932355 (S16); 26079066 (S17)
                    //anything after: 1531004716 (S15); 1787069365 (S16); 482365574 (S17)

                    if (item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.First100Ranks}"].level == 100)
                    {
                        int extraLevel = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.Above100Ranks}"].level;
                        Level = 100 + extraLevel;
                        XPProgress = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.Above100Ranks}"].progressToNextLevel;
                    }
                    else
                    {
                        Level = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.First100Ranks}"].level;
                        XPProgress = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.First100Ranks}"].progressToNextLevel;
                    }

                    PowerBonus = item.Response.profileProgression.data.seasonalArtifact.powerBonus;

                    return Level;
                }
            }
            catch
            {
                XPProgress = -1;
                PowerBonus = -1;
                FireteamPrivacy = PrivacySetting.Open;
                CharacterId = null;
                ActivityHash = 0;
                return -1;
            }
        }

        public static int GetAFKValues(ulong DiscordID, out int XPProgress, out int PowerBonus, out string ErrorStatus, out long ActivityHash)
        {
            ErrorStatus = $"ResponseError";
            try
            {
                var dil = GetLinkedUser(DiscordID);
                using (var client = new HttpClient())
                {
                    int Level = 0;
                    client.DefaultRequestHeaders.Add("X-API-Key", BotConfig.BungieApiKey);
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {dil.AccessToken}");

                    var response = client.GetAsync($"https://www.bungie.net/Platform/Destiny2/" + dil.BungieMembershipType + "/Profile/" + dil.BungieMembershipID + "/?components=100,104,202,204,1000").Result;
                    var content = response.Content.ReadAsStringAsync().Result;
                    dynamic item = JsonConvert.DeserializeObject(content);

                    ErrorStatus = $"{item.ErrorStatus}";
                    if (!ErrorStatus.Equals("Success"))
                    {
                        XPProgress = -1;
                        PowerBonus = -1;
                        ActivityHash = 0;
                        return -1;
                    }

                    if (item.Response.profileTransitoryData.data == null)
                    {
                        ErrorStatus = $"PlayerNotOnline";
                        XPProgress = -1;
                        PowerBonus = -1;
                        ActivityHash = 0;
                        return -1;
                    }

                    if (item.Response.characterProgressions.data == null)
                    {
                        ErrorStatus = $"PlayerProgressionPrivate";
                        XPProgress = -1;
                        PowerBonus = -1;
                        ActivityHash = 0;
                        return -1;
                    }

                    //first 100 levels: 4095505052 (S15); 2069932355 (S16); 26079066 (S17)
                    //anything after: 1531004716 (S15); 1787069365 (S16); 482365574 (S17)

                    if (item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.First100Ranks}"].level == 100)
                    {
                        int extraLevel = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.Above100Ranks}"].level;
                        Level = 100 + extraLevel;
                        XPProgress = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.Above100Ranks}"].progressToNextLevel;
                    }
                    else
                    {
                        Level = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.First100Ranks}"].level;
                        XPProgress = item.Response.characterProgressions.data[$"{item.Response.profile.data.characterIds[0]}"].progressions[$"{BotConfig.Hashes.First100Ranks}"].progressToNextLevel;
                    }

                    // Figure out which character is most recent.
                    ActivityHash = 0;
                    DateTime mostRecentDate = new();
                    for (int i = 0; i < item.Response.profile.data.characterIds.Count; i++)
                    {
                        string charId = item.Response.profile.data.characterIds[i];
                        var activityTime = DateTime.Parse($"{item.Response.characterActivities.data[$"{charId}"].dateActivityStarted}");
                        //ulong activityHash = item.Response.characterActivities.data[$"{charId}"].currentActivityHash;
                        if (activityTime > mostRecentDate)
                        {
                            mostRecentDate = activityTime;
                            ActivityHash = item.Response.characterActivities.data[$"{charId}"].currentActivityHash;
                        }
                    }

                    PowerBonus = item.Response.profileProgression.data.seasonalArtifact.powerBonus;

                    return Level;
                }
            }
            catch
            {
                XPProgress = -1;
                PowerBonus = -1;
                ActivityHash = 0;
                return -1;
            }
        }

        public static async Task PostDailyResetUpdate(DiscordShardedClient Client)
        { 
            List<ulong> guildsWithKeptChannel = new();
            List<ulong> keptChannels = new();
            foreach (ulong ChannelID in AnnounceDailyLinks.ToList())
            {
                try
                {
                    var channel = Client.GetChannel(ChannelID) as SocketTextChannel;
                    var guildChannel = Client.GetChannel(ChannelID) as SocketGuildChannel;
                    if (channel == null)
                    {
                        Log.Information("[{Type}] Could not find channel {Id}. Removing this element.", "Data", ChannelID);
                        DeleteChannelFromRotationConfig(ChannelID, true);
                        continue;
                    }

                    if (!guildsWithKeptChannel.Contains(guildChannel.Guild.Id))
                    {
                        keptChannels.Add(ChannelID);
                        guildsWithKeptChannel.Add(guildChannel.Guild.Id);
                    }

                    foreach (var chan in guildChannel.Guild.TextChannels)
                    {
                        if (IsExistingLinkedChannel(chan.Id, true) && ChannelID != chan.Id && guildsWithKeptChannel.Contains(chan.Guild.Id) && !keptChannels.Contains(chan.Id))
                        {
                            Log.Information("[{Type}] Duplicate channel detected. Removing: {Id}", "Data", chan.Id);
                            DeleteChannelFromRotationConfig(chan.Id, true);
                        }
                    }

                    var msg = await channel.SendMessageAsync($"", embed: CurrentRotations.DailyResetEmbed().Build());
                    if (channel is SocketNewsChannel && channel.Guild.Id == BotConfig.SupportServerID)
                        await msg.CrosspostAsync();
                }
                catch (Exception x)
                {
                    //DeleteChannelFromRotationConfig(ChannelID, true);
                    Log.Warning("[{Type}] Reset Error on Channel: {Id}. {Exception}", "Data", ChannelID, x);
                }
            }
        }

        public static async Task PostWeeklyResetUpdate(DiscordShardedClient Client)
        {
            List<ulong> guildsWithKeptChannel = new();
            List<ulong> keptChannels = new();
            foreach (ulong ChannelID in AnnounceWeeklyLinks.ToList())
            {
                try
                {
                    var channel = Client.GetChannel(ChannelID) as SocketTextChannel;
                    var guildChannel = Client.GetChannel(ChannelID) as SocketGuildChannel;
                    if (channel == null)
                    {
                        Log.Information("[{Type}] Could not find channel {Id}. Removing this element.", "Data", ChannelID);
                        DeleteChannelFromRotationConfig(ChannelID, false);
                        continue;
                    }

                    if (!guildsWithKeptChannel.Contains(guildChannel.Guild.Id))
                    {
                        keptChannels.Add(ChannelID);
                        guildsWithKeptChannel.Add(guildChannel.Guild.Id);
                    }

                    foreach (var chan in guildChannel.Guild.TextChannels)
                    {
                        if (IsExistingLinkedChannel(chan.Id, false) && ChannelID != chan.Id && guildsWithKeptChannel.Contains(chan.Guild.Id) && !keptChannels.Contains(chan.Id))
                        {
                            Log.Information("[{Type}] Duplicate channel detected. Removing: {Id}", "Data", chan.Id);
                            DeleteChannelFromRotationConfig(chan.Id, false);
                        }
                    }

                    var msg = await channel.SendMessageAsync($"", embed: CurrentRotations.WeeklyResetEmbed().Build());
                    if (channel is SocketNewsChannel && channel.Guild.Id == BotConfig.SupportServerID)
                        await msg.CrosspostAsync();
                }
                catch (Exception x)
                {
                    //DeleteChannelFromRotationConfig(ChannelID, true);
                    Log.Warning("[{Type}] Reset Error on Channel: {Id}. {Exception}", "Data", ChannelID, x);
                }
            }
        }
    }
}
