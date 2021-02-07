﻿using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Norm.Database.Contexts;
using Norm.Database.Entities;
using Norm.Database.Requests.BaseClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Norm.Database.Requests
{
    public class GuildLogChannels
    {
        public class Upsert : DbRequest<GuildLogChannel>
        {
            public Upsert(ulong guildId, ulong channelId)
            {
                this.LogChannel = new GuildLogChannel { GuildId = guildId, ChannelId = channelId };
            }

            public GuildLogChannel LogChannel { get; }
        }

        public class UpsertHandler : DbRequestHandler<Upsert, GuildLogChannel>
        {
            public UpsertHandler(IDbContext db) : base(db) { }
            public override async Task<DbResult<GuildLogChannel>> Handle(Upsert request, CancellationToken cancellationToken)
            {
                GuildLogChannel logChannel = this.DbContext.GuildLogChannels.FirstOrDefault(channel => channel.GuildId == request.LogChannel.GuildId);
                EntityEntry<GuildLogChannel> entity;
                DbResult<GuildLogChannel> result;
                if (logChannel is not null)
                {
                    entity = this.DbContext.GuildLogChannels.Update(request.LogChannel);
                    result = new DbResult<GuildLogChannel>
                    {
                        Success = entity.State.Equals(EntityState.Modified),
                        Value = entity.Entity,
                    };
                }
                else
                {
                    entity = await this.DbContext.GuildLogChannels.AddAsync(request.LogChannel, cancellationToken); 
                    result = new DbResult<GuildLogChannel>
                    {
                        Success = entity.State.Equals(EntityState.Added),
                        Value = entity.Entity,
                    };
                }

                await this.DbContext.Context.SaveChangesAsync(cancellationToken);

                return result;
            }
        }

        public class Delete : DbRequest
        {
            public Delete(DiscordGuild guild) : this(guild.Id) { }

            public Delete(ulong guildId)
            {
                this.GuildId = guildId;
            }

            public ulong GuildId { get; }
        }

        public class DeleteHandler : DbRequestHandler<Delete>
        {
            public DeleteHandler(IDbContext dbContext) : base(dbContext) { }

            public override async Task<DbResult> Handle(Delete request, CancellationToken cancellationToken)
            {
                GuildLogChannel logChannel = this.DbContext.GuildLogChannels.FirstOrDefault(channel => channel.GuildId == request.GuildId);
                if (logChannel == null)
                {
                    return new DbResult
                    {
                        Success = true,
                    };
                }
                
                EntityEntry<GuildLogChannel> entity = this.DbContext.GuildLogChannels.Remove(logChannel);
                DbResult result = new DbResult
                {
                    Success = entity.State.Equals(EntityState.Deleted),
                };
                await this.DbContext.Context.SaveChangesAsync(cancellationToken);
                return result;
            }
        }

        public class GetGuildLogChannel : DbRequest<GuildLogChannel>
        {
            public GetGuildLogChannel(DiscordGuild guild) : this(guild.Id) { }

            public GetGuildLogChannel(ulong guildId)
            {
                this.GuildId = guildId;
            }

            public ulong GuildId { get; }
        }

        public class GetGuildEventsHandler : DbRequestHandler<GetGuildLogChannel, GuildLogChannel>
        {
            public GetGuildEventsHandler(IDbContext dbContext) : base(dbContext) { }

            public override async Task<DbResult<GuildLogChannel>> Handle(GetGuildLogChannel request, CancellationToken cancellationToken)
            {
                GuildLogChannel result = await this.DbContext.GuildLogChannels
                    .FirstOrDefaultAsync(channel => channel.GuildId == request.GuildId, cancellationToken: cancellationToken);
                return new DbResult<GuildLogChannel>
                {
                    Success = true,
                    Value = result,
                };
            }
        }
    }
}