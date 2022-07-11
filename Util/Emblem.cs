﻿using APIHelper;
using Discord;
using Levante.Configs;
using Levante.Helpers;
using Newtonsoft.Json;
using System.Net.Http;
using HtmlAgilityPack;
using System.Net;
using System;

namespace Levante.Util
{
    public class Emblem : InventoryItem
    {
        public Emblem(long hashCode)
        {
            HashCode = hashCode;
            APIUrl = $"https://www.bungie.net/platform/Destiny2/Manifest/DestinyInventoryItemDefinition/" + HashCode;

            Content = ManifestConnection.GetInventoryItemById(unchecked((int)hashCode));
        }

        public int[] GetRGBAsIntArray()
        {
            int[] result = new int[3];
            result[0] = (int)Content.BackgroundColor.Red;
            result[1] = (int)Content.BackgroundColor.Green;
            result[2] = (int)Content.BackgroundColor.Blue;
            return result;
        }

        public string GetSourceString()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("X-API-Key", BotConfig.BungieApiKey);

                    var response = client.GetAsync($"https://www.bungie.net/platform/Destiny2/Manifest/DestinyCollectibleDefinition/" + GetCollectableHash()).Result;
                    var content = response.Content.ReadAsStringAsync().Result;
                    dynamic item = JsonConvert.DeserializeObject(content);
                    return item.Response.sourceString;
                }
            }
            catch
            {
                return "";
            }
        }

        public string GetBackgroundUrl() => "https://www.bungie.net" + Content.SecondaryIcon;

        public static bool HashIsAnEmblem(long HashCode)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-API-Key", BotConfig.BungieApiKey);

                var response = client.GetAsync($"https://www.bungie.net/platform/Destiny2/Manifest/DestinyInventoryItemDefinition/" + HashCode).Result;
                var content = response.Content.ReadAsStringAsync().Result;
                dynamic item = JsonConvert.DeserializeObject(content);
                string displayType = $"{item.Response.itemTypeDisplayName}";
                if (!displayType.Equals($"Emblem"))
                    return false;
                else
                    return true;
            }
        }

        // Use DEC's information on how to unlock an emblem, if DEC has it in their data.
        public string GetEmblemUnlock()
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(new WebClient().DownloadString($"https://destinyemblemcollector.com/emblem?id={HashCode}"));
                var emblemUnlock = doc.DocumentNode.SelectNodes("//div[@class='gridemblem-emblemdetail']")[8].InnerHtml;
                return emblemUnlock.Split("<li>")[1].Split("</li>")[0];
            }
            catch
            {
                // lazy catchall, but it just seems to hit internal server error if id isn't found
                return "";
            }
        }

        public override EmbedBuilder GetEmbed()
        {
            Console.WriteLine(GetIconUrl());
            var auth = new EmbedAuthorBuilder()
            {
                Name = $"Emblem Details: {GetName()}",
                IconUrl = GetIconUrl(),
            };
            var foot = new EmbedFooterBuilder()
            {
                Text = $"Powered by the Bungie API"
            };
            int[] emblemRGB = GetRGBAsIntArray();
            var embed = new EmbedBuilder()
            {
                Color = new Discord.Color(emblemRGB[0], emblemRGB[1], emblemRGB[2]),
                Author = auth,
                Footer = foot
            };
            try
            {
                var unlock = GetEmblemUnlock();
                string offerStr = "Unavailable or is not a limited-time offer.";
                if (EmblemOffer.HasExistingOffer(GetItemHash()))
                {
                    var offer = EmblemOffer.GetSpecificOffer(GetItemHash());
                    if (offer.StartDate > DateTime.Now)
                        offerStr = $"This emblem is [available]({offer.SpecialUrl}) {TimestampTag.FromDateTime(offer.StartDate, TimestampTagStyles.Relative)}!\n";
                    else
                        offerStr = $"This emblem is [currently available]({offer.SpecialUrl})!\n";
                }
                
                if (BotConfig.UniversalCodes.Exists(x => x.Name.Equals(GetName())))
                {
                    var uniCode = BotConfig.UniversalCodes.Find(x => x.Name.Equals(GetName()));
                    offerStr = $"This emblem is available via a code: {uniCode.Code}.\nRedeem it [here](https://www.bungie.net/7/en/Codes/Redeem).";
                }

                embed.Description = (GetSourceString().Equals("") ? "No source data provided." : GetSourceString()) + "\n";
                embed.ImageUrl = GetBackgroundUrl();
                embed.ThumbnailUrl = GetIconUrl();

                embed.AddField(x =>
                {
                    x.Name = "Hash Code";
                    x.Value = $"{GetItemHash()}";
                    x.IsInline = true;
                }).AddField(x =>
                {
                    x.Name = "Collectible Hash";
                    x.Value = $"{GetCollectableHash()}";
                    x.IsInline = true;
                })
                .AddField(x =>
                {
                    x.Name = "Availbility";
                    x.Value = $"{offerStr}";
                    x.IsInline = false;
                });

                if (!string.IsNullOrEmpty(unlock))
                {
                    embed.AddField(x =>
                    {
                        x.Name = "Unlock";
                        x.Value = $"[DEC](https://destinyemblemcollector.com/emblem?id={GetItemHash()}): {unlock}";
                        x.IsInline = false;
                    });
                }
            }
            catch
            {
                embed.Description = "This emblem is missing some API values, sorry about that!";
            }
            
            return embed;
        }
    }

    public class EmblemSearch
    {
        private long HashCode;
        private string Name;

        public EmblemSearch(long hashCode, string name)
        {
            HashCode = hashCode;
            Name = name;
        }

        public string GetName() => Name;

        public long GetEmblemHash() => HashCode;
    }
}
