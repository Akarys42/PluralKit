using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PluralKit.Bot {
    public static class ContextUtils {
                public static async Task<bool> PromptYesNo(this ICommandContext ctx, IUserMessage message, TimeSpan? timeout = null) {
            await message.AddReactionsAsync(new[] {new Emoji(Emojis.Success), new Emoji(Emojis.Error)});
            var reaction = await ctx.AwaitReaction(message, ctx.User, (r) => r.Emote.Name == Emojis.Success || r.Emote.Name == Emojis.Error, timeout ?? TimeSpan.FromMinutes(1));
            return reaction.Emote.Name == Emojis.Success;
        }

        public static async Task<SocketReaction> AwaitReaction(this ICommandContext ctx, IUserMessage message, IUser user = null, Func<SocketReaction, bool> predicate = null, TimeSpan? timeout = null) {
            var tcs = new TaskCompletionSource<SocketReaction>();
            Task Inner(Cacheable<IUserMessage, ulong> _message, ISocketMessageChannel _channel, SocketReaction reaction) {
                if (message.Id != _message.Id) return Task.CompletedTask; // Ignore reactions for different messages
                if (user != null && user.Id != reaction.UserId) return Task.CompletedTask; // Ignore messages from other users if a user was defined
                if (predicate != null && !predicate.Invoke(reaction)) return Task.CompletedTask; // Check predicate
                tcs.SetResult(reaction);
                return Task.CompletedTask;
            }

            (ctx.Client as BaseSocketClient).ReactionAdded += Inner;
            try {
                return await (tcs.Task.TimeoutAfter(timeout));
            } finally {
                (ctx.Client as BaseSocketClient).ReactionAdded -= Inner;
            }
        }

        public static async Task<IUserMessage> AwaitMessage(this ICommandContext ctx, IMessageChannel channel, IUser user = null, Func<SocketMessage, bool> predicate = null, TimeSpan? timeout = null) {
            var tcs = new TaskCompletionSource<IUserMessage>();
            Task Inner(SocketMessage msg) {
                if (channel != msg.Channel) return Task.CompletedTask; // Ignore messages in a different channel
                if (user != null && user != msg.Author) return Task.CompletedTask; // Ignore messages from other users
                if (predicate != null && !predicate.Invoke(msg)) return Task.CompletedTask; // Check predicate

                (ctx.Client as BaseSocketClient).MessageReceived -= Inner;
                tcs.SetResult(msg as IUserMessage);
                
                return Task.CompletedTask;
            }

            (ctx.Client as BaseSocketClient).MessageReceived += Inner;
            return await (tcs.Task.TimeoutAfter(timeout));
        }

        public static async Task Paginate<T>(this ICommandContext ctx, ICollection<T> items, int itemsPerPage, string title, Action<EmbedBuilder, IEnumerable<T>> renderer) {
            var pageCount = (items.Count / itemsPerPage) + 1;
            Embed MakeEmbedForPage(int page) {
                var eb = new EmbedBuilder();
                eb.Title = pageCount > 1 ? $"[{page+1}/{pageCount}] {title}" : title;
                renderer(eb, items.Skip(page*itemsPerPage).Take(itemsPerPage));
                return eb.Build();
            }

            var msg = await ctx.Channel.SendMessageAsync(embed: MakeEmbedForPage(0));
            var botEmojis = new[] { new Emoji("\u23EA"), new Emoji("\u2B05"), new Emoji("\u27A1"), new Emoji("\u23E9"), new Emoji(Emojis.Error) };
            await msg.AddReactionsAsync(botEmojis);

            try {
                var currentPage = 0;
                while (true) {
                    var reaction = await ctx.AwaitReaction(msg, ctx.User, timeout: TimeSpan.FromMinutes(5));

                    // Increment/decrement page counter based on which reaction was clicked
                    if (reaction.Emote.Name == "\u23EA") currentPage = 0; // <<
                    if (reaction.Emote.Name == "\u2B05") currentPage = (currentPage - 1) % pageCount; // <
                    if (reaction.Emote.Name == "\u27A1") currentPage = (currentPage + 1) % pageCount; // >
                    if (reaction.Emote.Name == "\u23E9") currentPage = pageCount - 1; // >>
                    if (reaction.Emote.Name == Emojis.Error) break; // X
                    
                    // If we can, remove the user's reaction (so they can press again quickly)
                    if (await ctx.HasPermission(ChannelPermission.ManageMessages) && reaction.User.IsSpecified) await msg.RemoveReactionAsync(reaction.Emote, reaction.User.Value);
                    
                    // Edit the embed with the new page
                    await msg.ModifyAsync((mp) => mp.Embed = MakeEmbedForPage(currentPage));
                }
            } catch (TimeoutException) {
                // "escape hatch", clean up as if we hit X
            }

            if (await ctx.HasPermission(ChannelPermission.ManageMessages)) await msg.RemoveAllReactionsAsync();
            else await msg.RemoveReactionsAsync(ctx.Client.CurrentUser, botEmojis);
        }

        public static async Task<ChannelPermissions> Permissions(this ICommandContext ctx) {
            if (ctx.Channel is IGuildChannel) {
                var gu = await ctx.Guild.GetCurrentUserAsync();
                return gu.GetPermissions(ctx.Channel as IGuildChannel);
            }
            return ChannelPermissions.DM;
        }

        public static async Task<bool> HasPermission(this ICommandContext ctx, ChannelPermission permission) => (await Permissions(ctx)).Has(permission);
    }
}