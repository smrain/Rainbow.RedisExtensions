using Rainbow.RedisExtensions.Utils;
using StackExchange.Redis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Rainbow.RedisExtensions
{
    public abstract class BaseRedisClient : IRedisClient
    {
        private static int _dbNumber = -1;
        private static string _redisConnectionString;
        private static IRedisSerializer _serializer;
        private static Lazy<ConnectionMultiplexer> _redis;
        private static IDatabase _database;

        private static readonly TimeSpan DEFAULT_EXPIRY_TIME = TimeSpan.FromSeconds(15);
        private static readonly string DEFAULT_LOCK_OWNER = Environment.MachineName;


        private static readonly object _locker = new object();

        protected abstract string GetRedisConnectionString();

        protected BaseRedisClient(IRedisSerializer serializer) : this(_dbNumber, serializer) { }

        protected BaseRedisClient(int dbNum, IRedisSerializer serializer) : this(dbNum, null, serializer) { }

        protected BaseRedisClient(int dbNum, string redisConfiguration, IRedisSerializer serializer)
        {
            _dbNumber = dbNum;
            _redisConnectionString = redisConfiguration ?? GetRedisConnectionString();
            _serializer = serializer;
            if (string.IsNullOrWhiteSpace(_redisConnectionString)) throw new ArgumentNullException(nameof(redisConfiguration));
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));

            Initial();
        }

        private void Initial()
        {
            if (_redis == null)
            {
                lock (_locker)
                {
                    if (_redis == null)
                    {
                        _redis = new Lazy<ConnectionMultiplexer>(ConnectionMultiplexer.Connect(_redisConnectionString));

                        // Register event delegate
                        _redis.Value.ConnectionFailed += OnConnectionFailed;
                        _redis.Value.ConnectionRestored += OnConnectionRestored;
                        _redis.Value.ErrorMessage += OnErrorMessage;
                        _redis.Value.ConfigurationChanged += OnConfigurationChanged;
                        _redis.Value.HashSlotMoved += OnHashSlotMoved;
                        _redis.Value.InternalError += OnInternalError;

                    }
                }
            }

            _database = _redis.Value.GetDatabase(_dbNumber);
        }


        #region event delegate
        /// <summary>
        /// 连接失败 ， 如果重新连接成功你将不会收到这个通知
        /// </summary>
        protected virtual void OnConnectionFailed(object sender, ConnectionFailedEventArgs e)
        {
            Console.WriteLine("重新连接：Endpoint failed: " + e.EndPoint + ", " + e.FailureType + (e.Exception == null ? "" : (", " + e.Exception.Message)));
        }

        /// <summary>
        /// 重新建立连接之前的错误
        /// </summary>
        protected virtual void OnConnectionRestored(object sender, ConnectionFailedEventArgs e)
        {
            Console.WriteLine("ConnectionRestored: " + e.EndPoint);
        }

        /// <summary>
        /// 发生错误时
        /// </summary>
        protected virtual void OnErrorMessage(object sender, RedisErrorEventArgs e)
        {
            Console.WriteLine("ErrorMessage: " + e.Message);
        }

        /// <summary>
        /// 配置更改时
        /// </summary>
        protected virtual void OnConfigurationChanged(object sender, EndPointEventArgs e)
        {
            Console.WriteLine("Configuration changed: " + e.EndPoint);
        }

        /// <summary>
        /// 更改集群
        /// </summary>
        protected virtual void OnHashSlotMoved(object sender, HashSlotMovedEventArgs e)
        {
            Console.WriteLine("HashSlotMoved:NewEndPoint" + e.NewEndPoint + ", OldEndPoint" + e.OldEndPoint);
        }

        /// <summary>
        /// redis类库错误
        /// </summary>
        protected virtual void OnInternalError(object sender, InternalErrorEventArgs e)
        {
            Console.WriteLine("InternalError:Message" + e.Exception.Message);
        }

        #endregion


        protected virtual TResult Excute<TResult>(Func<IDatabase, TResult> func)
        {
            TResult result = default;
            if (_database != null && func != null)
            {
                try
                {
                    IDatabase db = _database;
                    result = func.Invoke(db);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return result;
        }

        protected virtual void Excute(Action<IDatabase> action)
        {
            if (_database != null && action != null)
            {
                try
                {
                    IDatabase db = _database;
                    action.Invoke(db);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        protected virtual async Task<TResult> ExcuteAsync<TResult>(Func<IDatabaseAsync, Task<TResult>> func)
        {
            TResult result = default;
            if (_database != null && func != null)
            {
                try
                {
                    IDatabaseAsync db = _database;
                    result = await func(db).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return result;
        }

        protected virtual async Task ExcuteAsync(Func<IDatabaseAsync, Task> action)
        {
            if (_database != null && action != null)
            {
                try
                {
                    IDatabaseAsync db = _database;
                    await action(db).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }


        /// <summary>
        /// 分布式锁
        /// </summary>
        /// <param name="lockKey">锁的名称（不可重复）</param>
        /// <param name="func">需要执行的操作</param>
        /// <param name="lockTimeout">锁的有效时间（默认15S）</param>
        public virtual bool ExcuteWithLock(string lockKey, Action<IDatabase> func, TimeSpan? lockTimeout = default)
        {
            if (_database != null && func != null)
            {
                IDatabase db = _database;

                if (db.LockTake(lockKey, DEFAULT_LOCK_OWNER, lockTimeout ?? DEFAULT_EXPIRY_TIME))
                {
                    try
                    {
                        func.Invoke(db);
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                    finally
                    {
                        db.LockRelease(lockKey, DEFAULT_LOCK_OWNER); //释放锁
                    }

                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Transaction 事务
        /// </summary>
        /// <param name="action">需要执行的操作</param>
        /// <param name="flags">CommandFlags</param>
        /// <returns></returns>
        public virtual bool ExcuteWithTransaction(Action<ITransaction> action, CommandFlags flags = CommandFlags.None)
        {
            if (_database != null && action != null)
            {
                try
                {
                    ITransaction tran = _database.CreateTransaction();

                    action(tran);
                    return tran.Execute(flags);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return false;
        }

        /// <summary>
        /// Batch 批量操作
        /// </summary>
        /// <param name="action">需要执行的操作</param>
        public virtual void ExcuteWithBatch(Action<IBatch> action)
        {
            if (_database != null && action != null)
            {
                IBatch batch = _database.CreateBatch();

                try
                {
                    action(batch);

                    batch.Execute();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }


        public virtual TResult StringGet<TResult>(string key, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            var value = Excute((db) => db.StringGet(key, flags));
            KeyExpire(key, expiry);
            return ToGenericValue<TResult>(value);
        }

        public virtual async Task<TResult> StringGetAsync<TResult>(string key, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            var value = await ExcuteAsync((db) => db.StringGetAsync(key, flags));
            await KeyExpireAsync(key, expiry);
            return ToGenericValue<TResult>(value);
        }

        public virtual bool StringSet<TResult>(string key, TResult value, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return Excute(db => db.StringSet(key, redisValue, expiry, When.Always, flags));
        }

        public virtual async Task<bool> StringSetAsync<TResult>(string key, TResult value, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.StringSetAsync(key, redisValue, expiry, When.Always, flags));
        }


        public virtual TResult HashGet<TResult>(string key, string hashField, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
            where TResult : IDictionary
        {
            var value = Excute((db) =>
                string.IsNullOrWhiteSpace(hashField) ? db.HashGetAll(key) :
              (db.HashExists(key, hashField) ? new HashEntry[] { new HashEntry(hashField, db.HashGet(key, hashField, flags)) } : null));

            if (value == null) return default;
            KeyExpire(key, expiry);

            return ToGenericValue<TResult>(value);
        }

        public virtual async Task<TResult> HashGetAsync<TResult>(string key, string hashField, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
             where TResult : IDictionary
        {
            var value = await ExcuteAsync(async (db) =>
                string.IsNullOrWhiteSpace(hashField) ? await db.HashGetAllAsync(key) :
              (await db.HashExistsAsync(key, hashField) ? new HashEntry[] { new HashEntry(hashField, await db.HashGetAsync(key, hashField, flags)) } : null));

            if (value == null) return default;
            await KeyExpireAsync(key, expiry);

            return ToGenericValue<TResult>(value);
        }

        public virtual bool HashSet<TResult>(string key, TResult value, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
            where TResult : IDictionary
        {
            HashEntry[] redisValue = ToHashEntryArray(value);
            Excute((db) => db.HashSet(key, redisValue, flags));
            return true;
        }

        public virtual async Task<bool> HashSetAsync<TResult>(string key, TResult value, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
            where TResult : IDictionary
        {
            HashEntry[] redisValue = ToHashEntryArray(value);
            await ExcuteAsync((db) => db.HashSetAsync(key, redisValue, flags));
            return true;
        }


        /// <summary>
        /// 队列-入队
        /// </summary>
        /// <remarks>
        /// 队列：先进先出（FIFO-first in first out）:最先插入的元素最先出来。
        /// </remarks>
        /// <returns>入队后队列的长度 </returns>
        public virtual long Enqueue<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return Excute(db => db.ListLeftPush(key, redisValue, When.Always, flags));
        }

        /// <summary>
        /// 队列-入队（异步）
        /// </summary>
        /// <remarks>
        /// 队列：先进先出（FIFO-first in first out）:最先插入的元素最先出来。
        /// </remarks>
        /// <returns>入队后队列的长度 </returns>
        public virtual async Task<long> EnqueueAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.ListLeftPushAsync(key, redisValue, When.Always, flags));
        }

        /// <summary>
        /// 队列-出队
        /// </summary>
        /// <remarks>
        /// 队列：先进先出（FIFO-first in first out）:最先插入的元素最先出来。
        /// </remarks>
        /// <returns>出队的值 </returns>
        public virtual TResult Dequeue<TResult>(string key, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            var value = Excute((db) => db.ListRightPop(key, flags));
            KeyExpire(key, expiry);
            return ToGenericValue<TResult>(value);
        }

        /// <summary>
        /// 队列-出队
        /// </summary>
        /// <remarks>
        /// 队列：先进先出（FIFO-first in first out）:最先插入的元素最先出来。
        /// </remarks>
        /// <returns>出队的值 </returns>
        public virtual async Task<TResult> DequeueAsync<TResult>(string key, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            var value = await ExcuteAsync((db) => db.ListRightPopAsync(key, flags));
            await KeyExpireAsync(key, expiry);
            return ToGenericValue<TResult>(value);
        }


        /// <summary>
        /// 栈-入栈
        /// </summary>
        /// <remarks>
        /// 栈：后进先出（LIFO-last in first out）:最后插入的元素最先出来。
        /// </remarks>
        /// <returns>入栈后栈的长度 </returns>
        public virtual long Enstack<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return Excute(db => db.ListRightPush(key, redisValue, When.Always, flags));
        }

        /// <summary>
        /// 栈-入栈（异步）
        /// </summary>
        /// <remarks>
        /// 栈：后进先出（LIFO-last in first out）:最后插入的元素最先出来。
        /// </remarks>
        /// <returns>入栈后栈的长度 </returns>
        public virtual async Task<long> EnstackAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.ListRightPushAsync(key, redisValue, When.Always, flags));
        }

        /// <summary>
        /// 栈-出栈
        /// </summary>
        /// <remarks>
        /// 栈：后进先出（LIFO-last in first out）:最后插入的元素最先出来。
        /// </remarks>
        /// <returns>出栈的值 </returns>
        public virtual TResult Destack<TResult>(string key, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            var value = Excute((db) => db.ListRightPop(key, flags));
            KeyExpire(key, expiry);
            return ToGenericValue<TResult>(value);
        }

        /// <summary>
        /// 栈-出栈（异步）
        /// </summary>
        /// <remarks>
        /// 栈：后进先出（LIFO-last in first out）:最后插入的元素最先出来。
        /// </remarks>
        /// <returns>出栈的值 </returns>
        public virtual async Task<TResult> DestackAsync<TResult>(string key, TimeSpan? expiry = default, CommandFlags flags = CommandFlags.None)
        {
            var value = await ExcuteAsync((db) => db.ListRightPopAsync(key, flags));
            await KeyExpireAsync(key, expiry);
            return ToGenericValue<TResult>(value);
        }

        /// <summary>
        /// 分页查询List
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="key"></param>
        /// <param name="pageIndex">页索引</param>
        /// <param name="pageSize">分页大小</param>
        /// <param name="flags"></param>
        /// <returns>item1：数据明细 item2：总条数</returns>
        public virtual (IEnumerable<TResult>, long) ListGetPaged<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, CommandFlags flags = CommandFlags.None)
        {
            var value = Excute(
                db =>
                {
                    long strat = (pageIndex == null || pageSize == null) ? 0 : (pageIndex.Value - 1) * pageSize.Value;
                    long stop = (pageIndex == null || pageSize == null) ? -1 : pageSize.Value - 1;

                    var len = db.ListLength(key, flags);
                    var data = db.ListRange(key, strat, stop, flags);
                    return (data, len);
                });
            return (ToGenericValue<TResult>(value.data), value.len);
        }

        /// <summary>
        /// 分页查询List（异步）
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="key"></param>
        /// <param name="pageIndex">页索引</param>
        /// <param name="pageSize">分页大小</param>
        /// <param name="flags"></param>
        /// <returns>item1：数据明细 item2：总条数</returns>
        public virtual async Task<(IEnumerable<TResult>, long)> ListGetPagedAsync<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, CommandFlags flags = CommandFlags.None)
        {
            var value = await ExcuteAsync(
              async db =>
               {
                   long strat = (pageIndex == null || pageSize == null) ? 0 : (pageIndex.Value - 1) * pageSize.Value;
                   long stop = (pageIndex == null || pageSize == null) ? -1 : pageSize.Value - 1;

                   var len = await db.ListLengthAsync(key, flags);
                   var data = await db.ListRangeAsync(key, strat, stop, flags);
                   return (data, len);
               });
            return (ToGenericValue<TResult>(value.data), value.len);
        }


        public virtual bool SetAdd<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return Excute(db => db.SetAdd(key, redisValue, flags));
        }

        public virtual async Task<bool> SetAddAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.SetAddAsync(key, redisValue, flags));
        }

        public virtual long SetAdd<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue[] redisValue = ToRedisValue(value);
            return Excute(db => db.SetAdd(key, redisValue, flags));
        }

        public virtual async Task<long> SetAddAsync<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue[] redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.SetAddAsync(key, redisValue, flags));
        }

        public virtual bool SetRemove<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return Excute(db => db.SetRemove(key, redisValue, flags));
        }

        public virtual async Task<bool> SetRemoveAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.SetRemoveAsync(key, redisValue, flags));
        }

        public virtual long SetRemove<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue[] redisValue = ToRedisValue(value);
            return Excute(db => db.SetRemove(key, redisValue, flags));
        }

        public virtual async Task<long> SetRemoveAsync<TResult>(string key, IEnumerable<TResult> value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue[] redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.SetRemoveAsync(key, redisValue, flags));
        }

        public virtual IEnumerable<TResult> SetGetList<TResult>(string key, CommandFlags flags = CommandFlags.None)
        {
            var value = Excute(db => db.SetMembers(key, flags));
            return ToGenericValue<TResult>(value);
        }

        public virtual async Task<IEnumerable<TResult>> SetGetListAsync<TResult>(string key, CommandFlags flags = CommandFlags.None)
        {
            var value = await ExcuteAsync(db => db.SetMembersAsync(key, flags));
            return ToGenericValue<TResult>(value);
        }


        public virtual bool SortedSetAdd<TResult>(string key, TResult value, double score, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return Excute(db => db.SortedSetAdd(key, redisValue, score, flags));
        }

        public virtual async Task<bool> SortedSetAddAsync<TResult>(string key, TResult value, double score, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.SortedSetAddAsync(key, redisValue, score, flags));
        }

        public virtual bool SortedSetRemove<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return Excute(db => db.SortedSetRemove(key, redisValue, flags));
        }

        public virtual async Task<bool> SortedSetRemoveAsync<TResult>(string key, TResult value, CommandFlags flags = CommandFlags.None)
        {
            RedisValue redisValue = ToRedisValue(value);
            return await ExcuteAsync(db => db.SortedSetRemoveAsync(key, redisValue, flags));
        }

        /// <summary>
        /// 根据排名分页查询SortedSet
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="key">key</param>
        /// <param name="pageIndex">页索引</param>
        /// <param name="pageSize">分页大小</param>
        /// <param name="order">排序</param>
        /// <param name="flags">CommandFlags</param>
        /// <returns>item1：数据明细 item2：总条数</returns>
        public virtual (IEnumerable<TResult>, long) SortedSetByRankGetPaged<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var value = Excute(
                db =>
                {
                    long strat = (pageIndex == null || pageSize == null) ? 0 : (pageIndex.Value - 1) * pageSize.Value;
                    long stop = (pageIndex == null || pageSize == null) ? -1 : pageSize.Value - 1;

                    var len = db.SortedSetLength(key);
                    var data = db.SortedSetRangeByRank(key, strat, stop, order, flags);
                    return (data, len);
                });
            return (ToGenericValue<TResult>(value.data), value.len);
        }

        /// <summary>
        /// 根据排名分页查询SortedSet（异步）
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="key">key</param>
        /// <param name="pageIndex">页索引</param>
        /// <param name="pageSize">分页大小</param>
        /// <param name="order">排序</param>
        /// <param name="flags">CommandFlags</param>
        /// <returns>item1：数据明细 item2：总条数</returns>
        public virtual async Task<(IEnumerable<TResult>, long)> SortedSetByRankGetPagedAsync<TResult>(string key, int? pageIndex = 1, int? pageSize = 10, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        {
            var value = await ExcuteAsync(
                async db =>
                {
                    long strat = (pageIndex == null || pageSize == null) ? 0 : (pageIndex.Value - 1) * pageSize.Value;
                    long stop = (pageIndex == null || pageSize == null) ? -1 : pageSize.Value - 1;

                    var len = await db.SortedSetLengthAsync(key);
                    var data = await db.SortedSetRangeByRankAsync(key, strat, stop, order, flags);
                    return (data, len);
                });
            return (ToGenericValue<TResult>(value.data), value.len);
        }


        public virtual bool KeyExpire(string key, TimeSpan? expiry = default)
        {
            return expiry.HasValue && Excute((db) => db.KeyExpire(key, expiry));
        }
        public virtual async Task<bool> KeyExpireAsync(string key, TimeSpan? expiry = default)
        {
            return expiry.HasValue && (await ExcuteAsync((db) => db.KeyExpireAsync(key, expiry)));
        }

        public virtual bool Delete(string key)
        {
            return Excute((db) => db.KeyDelete(key));
        }
        public virtual async Task<bool> DeleteAsync(string key)
        {
            return await ExcuteAsync((db) => db.KeyDeleteAsync(key));
        }

        public virtual bool HashDelete(string key, string hashField)
        {
            return Excute((db) => db.HashDelete(key, hashField));
        }
        public virtual async Task<bool> HashDeleteAsync(string key, string hashField)
        {
            return await ExcuteAsync((db) => db.HashDeleteAsync(key, hashField));
        }


        #region private method
        protected static RedisValue ToRedisValue(object obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            var valueType = obj.GetType();

            if (valueType.IsRedisValueType() || valueType == typeof(RedisValue))
            {
                return RedisValue.Unbox(obj);
            }
            else if (valueType.IsSimpleType())
            {
                return obj.ToString();
            }

            return _serializer.Serialize(obj);
        }
        protected static RedisValue[] ToRedisValue<TResult>(IEnumerable<TResult> obj)
        {
            if (obj == null) throw new ArgumentException(nameof(obj));

            var result = new List<RedisValue>();
            obj.ToList().ForEach((item) =>
            {
                var rdsValue = ToRedisValue(item);
                result.Add(rdsValue);
            });
            return result?.ToArray();
        }
        protected static HashEntry[] ToHashEntryArray(IDictionary obj)
        {
            if (obj == null) throw new ArgumentException(nameof(obj));
            var value = new List<HashEntry>();
            foreach (var key in obj.Keys)
            {
                if (key == null) continue;
                value.Add(new HashEntry(key.ToString(), _serializer.Serialize(obj[key])));
            }
            return value.Any() ? value.ToArray() : null;
        }
        protected static TResult ToGenericValue<TResult>(HashEntry[] obj)
            where TResult : IDictionary
        {
            var result = default(TResult);

            var resultType = typeof(TResult);
            var paramTypes = resultType.GetGenericArguments();
            var dicInstance = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(paramTypes), true);
            var addMethod = dicInstance.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            obj.ToList().ForEach(item =>
            {
                var key = Convert.ChangeType(item.Name, paramTypes[0]);
                var val = (paramTypes[1].IsSimpleType()) ? Convert.ChangeType(item.Value, paramTypes[1]) : _serializer.Deserialize(item.Value, paramTypes[1]);
                addMethod.Invoke(dicInstance, new object[] { key, val });
            });

            result = (TResult)dicInstance;

            return result;
        }
        protected static IEnumerable<TResult> ToGenericValue<TResult>(RedisValue[] obj)
        {
            if (obj == null || !obj.Any()) return default;
            var result = new List<TResult>();

            obj.ToList().ForEach(item =>
            {
                var val = ToGenericValue<TResult>(item);
                result.Add(val);
            });
            return result;
        }
        protected static TResult ToGenericValue<TResult>(RedisValue obj)
        {
            var canChangeType = ReflectionHelper.TryChangeType(obj, typeof(TResult), out object value);
            return canChangeType ? (TResult)value : (TResult)_serializer.Deserialize(obj, typeof(TResult));
        }
        #endregion
    }
}
