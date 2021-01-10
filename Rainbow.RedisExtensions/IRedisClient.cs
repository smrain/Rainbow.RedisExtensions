using StackExchange.Redis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rainbow.RedisExtensions
{
    public interface IRedisClient
    {
        bool ExcuteWithLock(string lockKey, Action<IDatabase> func, TimeSpan? lockTimeout = null);
        bool ExcuteWithTransaction(Action<ITransaction> action, CommandFlags flags = CommandFlags.None);
        void ExcuteWithBatch(Action<IBatch> action);

        bool Delete(string key);
        Task<bool> DeleteAsync(string key);
        bool KeyExpire(string key, TimeSpan? expiry = null);
        Task<bool> KeyExpireAsync(string key, TimeSpan? expiry = null);

        TResult StringGet<TResult>(string key, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);
        Task<TResult> StringGetAsync<TResult>(string key, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);
        bool StringSet<TResult>(string key, TResult value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);
        Task<bool> StringSetAsync<TResult>(string key, TResult value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);

        TResult HashGet<TResult>(string key, string hashField, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None) where TResult : IDictionary;
        Task<TResult> HashGetAsync<TResult>(string key, string hashField, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None) where TResult : IDictionary;
        bool HashSet<TResult>(string key, TResult value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None) where TResult : IDictionary;
        Task<bool> HashSetAsync<TResult>(string key, TResult value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None) where TResult : IDictionary;
        bool HashDelete(string key, string hashField);
        Task<bool> HashDeleteAsync(string key, string hashField);

        long Enqueue<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        Task<long> EnqueueAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        TResult Dequeue<TResult>(string key, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);
        Task<TResult> DequeueAsync<TResult>(string key, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);
        long Enstack<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        Task<long> EnstackAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        TResult Destack<TResult>(string key, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);
        Task<TResult> DestackAsync<TResult>(string key, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);
        (IEnumerable<TResult>, long) ListGetPaged<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, CommandFlags flags = CommandFlags.None);
        Task<(IEnumerable<TResult>, long)> ListGetPagedAsync<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, CommandFlags flags = CommandFlags.None);

        long SetAdd<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None);
        bool SetAdd<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        Task<long> SetAddAsync<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None);
        Task<bool> SetAddAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        long SetRemove<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None);
        bool SetRemove<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        Task<long> SetRemoveAsync<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None);
        Task<bool> SetRemoveAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        IEnumerable<TResult> SetGetList<TResult>(string key, CommandFlags flags = CommandFlags.None);
        Task<IEnumerable<TResult>> SetGetListAsync<TResult>(string key, CommandFlags flags = CommandFlags.None);

        bool SortedSetAdd<TResult>(string key, TResult value, double score, CommandFlags flags = CommandFlags.None);
        Task<bool> SortedSetAddAsync<TResult>(string key, TResult value, double score, CommandFlags flags = CommandFlags.None);
        bool SortedSetRemove<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        Task<bool> SortedSetRemoveAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None);
        (IEnumerable<TResult>, long) SortedSetByRankGetPaged<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);
        Task<(IEnumerable<TResult>, long)> SortedSetByRankGetPagedAsync<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);
    }
}