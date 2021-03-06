using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stl.Async;
using Stl.Collections;
using Stl.Fusion;
using Stl.Fusion.Bridge;
using Samples.Blazor.Common.Services;
using Stl;

namespace Samples.Blazor.Server.Services
{
    public class ChatService : IChatService, IComputedService
    {
        private readonly ILogger _log;
        private readonly ChatDbContextPool _dbContextPool;
        private readonly   IUzbyClient _uzbyClient;
        private readonly  IForismaticClient _forismaticClient;
        private readonly  IPublisher _publisher;

        public ChatService(
            ChatDbContextPool dbContextPool,
            IUzbyClient uzbyClient,
            IForismaticClient forismaticClient,
            IPublisher publisher,
            ILogger<ChatService>? log = null)
        {
            _log = log ??= NullLogger<ChatService>.Instance;
            _dbContextPool = dbContextPool;
            _uzbyClient = uzbyClient;
            _forismaticClient = forismaticClient;
            _publisher = publisher;
        }

        // Writers

        public async Task<ChatUser> CreateUserAsync(string name, CancellationToken cancellationToken = default)
        {
            name = await NormalizeNameAsync(name, cancellationToken).ConfigureAwait(false);
            using var lease = _dbContextPool.Rent();
            var dbContext = lease.Subject;

            var userEntry = dbContext.Users.Add(new ChatUser() {
                Name = name
            });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var user = userEntry.Entity;

            // Invalidation
            Computed.Invalidate(() => GetUserAsync(user.Id, CancellationToken.None));
            Computed.Invalidate(() => GetUserCountAsync(CancellationToken.None));
            return user;
        }

        public async Task<ChatUser> SetUserNameAsync(long id, string name, CancellationToken cancellationToken = default)
        {
            name = await NormalizeNameAsync(name, cancellationToken).ConfigureAwait(false);
            using var lease = _dbContextPool.Rent();
            var dbContext = lease.Subject;

            var user = await GetUserAsync(id, cancellationToken).ConfigureAwait(false);
            user.Name = name;
            dbContext.Users.Update(user);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Invalidation
            Computed.Invalidate(() => GetUserAsync(id, CancellationToken.None));
            return user;
        }

        public async Task<ChatMessage> AddMessageAsync(long userId, string text, CancellationToken cancellationToken = default)
        {
            text = await NormalizeTextAsync(text, cancellationToken).ConfigureAwait(false);
            using var lease = _dbContextPool.Rent();
            var dbContext = lease.Subject;

            await GetUserAsync(userId, cancellationToken).ConfigureAwait(false); // Check to ensure the user exists
            var messageEntry = dbContext.Messages.Add(new ChatMessage() {
                CreatedAt = DateTime.UtcNow,
                UserId = userId,
                Text = text,
            });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var message = messageEntry.Entity;

            // Invalidation
            Computed.Invalidate(EveryChatTail);
            return message;
        }

        // Readers

        [ComputedServiceMethod]
        public virtual async Task<long> GetUserCountAsync(CancellationToken cancellationToken = default)
        {
            using var lease = _dbContextPool.Rent();
            var dbContext = lease.Subject;
            return await dbContext.Users.LongCountAsync(cancellationToken).ConfigureAwait(false);
        }

        [ComputedServiceMethod]
        public virtual Task<long> GetActiveUserCountAsync(CancellationToken cancellationToken = default)
        {
            var channelHub = _publisher.ChannelHub;
            var userCount = (long) channelHub.ChannelCount;
            var c = Computed.GetCurrent();
            Task.Run(async () => {
                do {
                    await Task.Delay(1000, default).ConfigureAwait(false);
                } while (userCount == channelHub.ChannelCount);
                c!.Invalidate();
            }, default).Ignore();
            return Task.FromResult(Math.Max(0, userCount));
        }

        [ComputedServiceMethod]
        public virtual async Task<ChatUser> GetUserAsync(long id, CancellationToken cancellationToken = default)
        {
            using var lease = _dbContextPool.Rent();
            var dbContext = lease.Subject;
            return await dbContext.Users
                .SingleAsync(u => u.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }

        [ComputedServiceMethod]
        public virtual async Task<ChatPage> GetChatTailAsync(int length, CancellationToken cancellationToken = default)
        {
            await EveryChatTail().ConfigureAwait(false);
            using var lease = _dbContextPool.Rent();
            var dbContext = lease.Subject;
            var messages = dbContext.Messages.OrderByDescending(m => m.Id).Take(length).ToList();
            messages.Reverse();
            var users = await Task.WhenAll(messages
                    .DistinctBy(m => m.UserId)
                    .Select(m => GetUserAsync(m.UserId, cancellationToken)))
                .ConfigureAwait(false);
            var userById = users.ToDictionary(u => u.Id);
            return new ChatPage(messages, userById);
        }

        [ComputedServiceMethod]
        public virtual Task<ChatPage> GetChatPageAsync(long minMessageId, long maxMessageId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        // Helpers

        [ComputedServiceMethod]
        protected virtual Task<Unit> EveryChatTail() => TaskEx.UnitTask;

        protected virtual async ValueTask<string> NormalizeNameAsync(string name, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(name))
                return name;
            name = await _uzbyClient
                .GetNameAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (name.GetHashCode() % 3 == 0)
                // First-last name pairs are fun too :)
                name += " " + await _uzbyClient
                    .GetNameAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            return name;
        }

        protected virtual async ValueTask<string> NormalizeTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(text))
                return text;
            var json = await _forismaticClient
                .GetQuoteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return json.Value<string>("quoteText");
        }
    }
}
