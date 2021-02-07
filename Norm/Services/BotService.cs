﻿using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using Hangfire;
using Norm.Configuration;
using Norm.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using MediatR;
using NodaTime;
using Norm.Formatters;
using DSharpPlus.CommandsNext.Converters;
using Norm.Database.Entities;
using Norm.Database.Requests;
using Microsoft.Extensions.Caching.Memory;

namespace Norm.Services
{
    public partial class BotService : IBotService
    {
        // Client and Extensions
        public DiscordShardedClient ShardedClient { get; private set; }
        private IReadOnlyDictionary<int, CommandsNextExtension> commandsDict;
        private IReadOnlyDictionary<int, InteractivityExtension> interactivityDict;

        // Shared between events
        public DiscordMember BotDeveloper { get; private set; }
        private ILogger Logger { get; }
        private IMediator Mediator { get; }
        private IDateTimeZoneProvider TimeZoneProvider { get; }

        // Configurations
        private readonly BotOptions config;
        private readonly DiscordConfiguration clientConfig;
        private readonly CommandsNextConfiguration commandsConfig;
        private readonly InteractivityConfiguration interactivityConfig;
        public IMemoryCache PrefixCache { get; }

        // Public properties
        public bool Started { get; private set; }

        public BotService(
            IOptions<BotOptions> options, 
            ILoggerFactory factory, 
            IServiceProvider provider, 
            IMediator mediator,
            IDateTimeZoneProvider timeZoneProvider)
        {
            this.Started = false;
            this.config = options.Value;
            this.Logger = factory.CreateLogger<BotService>();
            this.Mediator = mediator;
            this.TimeZoneProvider = timeZoneProvider;
            MemoryCacheOptions memCacheOpts = new MemoryCacheOptions
            {
                SizeLimit = 1000,
                CompactionPercentage = .25,
            };
            this.PrefixCache = new MemoryCache(memCacheOpts, factory);

            #region Client Config
            this.clientConfig = new DiscordConfiguration
            {
                Token = this.config.BotToken,
                TokenType = TokenType.Bot,
                MinimumLogLevel = LogLevel.Information,
                LoggerFactory = factory,
                Intents = DiscordIntents.AllUnprivileged,
            };
            #endregion

            #region Commands Module Config
            this.commandsConfig = new CommandsNextConfiguration
            {
                Services = provider,
                EnableDms = this.config.EnableDms,
                EnableMentionPrefix = this.config.EnableMentionPrefix
            };

            if (this.config.EnablePrefixResolver)
                this.commandsConfig.PrefixResolver = PrefixResolver;
            else
                this.commandsConfig.StringPrefixes = this.config.Prefixes;
            #endregion

            #region Interactivity Module Config
            this.interactivityConfig = new InteractivityConfiguration
            {
                PaginationBehaviour = PaginationBehaviour.Ignore,
                PaginationDeletion = PaginationDeletion.KeepEmojis,
                PollBehaviour = PollBehaviour.DeleteEmojis,
                Timeout = TimeSpan.FromMinutes(5),
            };
            #endregion
        }

        public async Task StartAsync()
        {
            this.ShardedClient = new DiscordShardedClient(clientConfig);

            this.commandsDict = await this.ShardedClient.UseCommandsNextAsync(this.commandsConfig);

            this.interactivityDict = await this.ShardedClient.UseInteractivityAsync(this.interactivityConfig);

            foreach (CommandsNextExtension commands in this.commandsDict.Values)
            {
                commands.RegisterCommands<GeneralModule>();
                commands.RegisterCommands<RoyalRoadModule>();
                commands.RegisterCommands<TimeModule>();
                commands.RegisterCommands<EventModule>();
                commands.RegisterCommands<PrefixModule>();
                commands.RegisterCommands<ModerationModule>();

                commands.CommandErrored += ChecksFailedError;
                commands.CommandErrored += this.CheckCommandExistsError;
                commands.CommandErrored += this.LogExceptions;

                commands.SetHelpFormatter<CategoryHelpFormatter>();

                commands.RegisterConverter(new EnumConverter<ModerationActionType>());
            }

            this.ShardedClient.Ready += ShardedClient_UpdateStatus;
            this.ShardedClient.GuildDownloadCompleted += ShardedClient_GuildDownloadCompleted;

            this.ShardedClient.MessageCreated += this.CheckForDate;
            this.ShardedClient.MessageReactionAdded += this.SendAdjustedDate;

            await this.ShardedClient.StartAsync();
        }

        public async Task StopAsync()
        {
            this.Started = false;
            await this.ShardedClient.StopAsync();
        }

        private async Task ShardedClient_UpdateStatus(DiscordClient sender, ReadyEventArgs e)
        {
            this.Started = true;
            await this.ShardedClient.UpdateStatusAsync(new DiscordActivity("^help", ActivityType.Watching));
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously which isn't a problem in this case
        private async Task ShardedClient_GuildDownloadCompleted(DiscordClient client, GuildDownloadCompletedEventArgs args)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            _ = Task.Run(async () =>
            {
                DiscordGuild botDevGuild = await client.GetGuildAsync(this.config.DevGuildId);
                this.BotDeveloper = await botDevGuild.GetMemberAsync(this.config.DevId);
                this.ClockEmoji = DiscordEmoji.FromName(client, ":clock:");
                RecurringJob.AddOrUpdate<AnnouncementService>(service => service.AnnounceUpdates(), "0/1 * * * *");
                

                await this.BotDeveloper.SendMessageAsync("Announcements have been started");
            });
        }

        private async Task<int> PrefixResolver(DiscordMessage msg)
        {
            if (!PrefixCache.TryGetValue(msg.Channel.Guild.Id, out GuildPrefix[] guildPrefixes))
            {
                guildPrefixes = (await this.Mediator.Send(new GuildPrefixes.GetGuildsPrefixes(msg.Channel.Guild))).Value.ToArray();
                MemoryCacheEntryOptions entryOpts = new MemoryCacheEntryOptions()
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    AbsoluteExpiration = DateTime.Now + TimeSpan.FromDays(1),
                    Size = guildPrefixes.Length,
                };

                PrefixCache.Set(msg.Channel.Guild.Id, guildPrefixes, entryOpts);
            }

            if (!guildPrefixes.Any())
            {
                return msg.GetStringPrefixLength("^");
            }

            foreach (GuildPrefix prefix in guildPrefixes)
            {
                var length = msg.GetStringPrefixLength(prefix.Prefix);
                if (length != -1)
                {
                    return length;
                }
            }

            return -1;
        }
    }
}