using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Rainbow.RedisExtensions.Tests
{
    public class RedisFunTest
    {
        /// <summary>
        /// 设置Redis数据序列化方法，Json序列化
        /// </summary>
        internal class JsonRedisSerializer : IRedisSerializer
        {
            private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings()
            {
                DateFormatString = "yyyy-MM-dd HH:mm:ss"
            };

            public string Serialize(object t)
            {
                return t is string ? t.ToString() : JsonConvert.SerializeObject(t, _settings);
            }

            public T Deserialize<T>(string content)
            {
                if (string.IsNullOrEmpty(content)) return default(T);
                return JsonConvert.DeserializeObject<T>(content, _settings);
            }

            public object Deserialize(string content, Type type)
            {
                if (string.IsNullOrEmpty(content)) return null;
                return JsonConvert.DeserializeObject(content, type, _settings);
            }
        }

        public sealed class RedisClientContext : BaseRedisClient
        {
            private static readonly IRedisSerializer serializer = new JsonRedisSerializer();
            public RedisClientContext() : base(serializer) { }

            //设置链接字符串
            protected override string GetRedisConnectionString() => "192.168.88.131:6379,defaultDatabase=0,password=pwd";


            /// <summary>
            /// iterates fields of Hash types and their associated values.
            /// </summary>
            /// <remarks>
            ///     Time complexity: O(1) for every call. O(N) for a complete iteration, including enough command calls for the cursor to return back to 0. 
            ///     N is the number of elements inside the collection.
            /// </remarks>
            /// <typepar am name="T">Type of the returned value</typeparam>
            /// <param name="hashKey">Key of the hash</param>
            /// <param name="pattern">GLOB search pattern</param>
            /// <param name="pageSize">Number of elements to retrieve from the redis server in the cursor</param>
            /// <param name="commandFlags">Command execution flags</param>
            /// <returns></returns>
            public TResult HashScan<TResult>(string hashKey, string pattern, CommandFlags commandFlags = CommandFlags.None)
                where TResult : IDictionary
            {
                var value = Excute((db) =>
                {
                    var values = new HashSet<HashEntry>();
                    int nextCursor = 0;
                    do
                    {
                        var redisResult = db.HashScan(hashKey, pattern, 1000, 0, nextCursor, commandFlags);
                        if (redisResult.Count() == 0) break;
                        var innerResult = redisResult.ToArray();
                        nextCursor += redisResult.Count();

                        values.UnionWith(innerResult);
                    } while (nextCursor != 0);

                    return values?.ToArray();
                });
                return ToGenericValue<TResult>(value);
            }

            /// <summary>
            /// Searches the keys from Redis database
            /// </summary>
            /// <remarks>
            ///     Consider this as a command that should only be used in production environments with extreme care. It may ruin
            ///     performance when it is executed against large databases
            /// </remarks>
            /// <param name="pattern">The pattern.</param>
            /// <example>
            ///  Redis 模糊匹配 SearchKeys
            ///  语法：KEYS pattern
            ///  说明：返回与指定模式相匹配的所用的keys。	
            ///  该命令所支持的匹配模式如下：	
            ///  （1）?：用于匹配单个字符。例如，h?llo可以匹配hello、hallo和hxllo等；	
            ///  （2）*：用于匹配零个或者多个字符。例如，h*llo可以匹配hllo和heeeello等；	
            ///  （3）[]：可以用来指定模式的选择区间。例如h[ae]llo可以匹配hello和hallo，但是不能匹配hillo。	
            ///  同时，可以使用“/”符号来转义特殊的字符
            /// </example>
            /// <returns>A list of cache keys retrieved from Redis database</returns>
            public IEnumerable<string> SearchKeys(string pattern)
            {
                var value = Excute((db) =>
                {
                    var keys = new HashSet<string>();

                    int nextCursor = 0;
                    do
                    {
                        RedisResult redisResult = db.Execute("SCAN", nextCursor.ToString(), "MATCH", pattern, "COUNT", "1000");
                        var innerResult = (RedisResult[])redisResult;

                        nextCursor = int.Parse((string)innerResult[0]);

                        List<string> resultLines = ((string[])innerResult[1]).ToList();

                        keys.UnionWith(resultLines);
                    } while (nextCursor != 0);
                    return keys;
                });

                return value;
            }
        }


        [Fact(DisplayName = "功能测试")]
        public void Test1()
        {
            var client = new RedisClientContext();

            //transaction
            var tran_result = client.ExcuteWithTransaction(
                tran =>
                {
                    tran.StringSetAsync("tran1", "aaa", TimeSpan.FromSeconds(30));
                    //tran.AddCondition(Condition.StringNotEqual("tran1", "11212"));
                    tran.StringDecrementAsync("tran1", 1);
                });

            //batch
            client.ExcuteWithBatch(
                 batch =>
                 {
                     batch.StringSetAsync("batch1", "11212", TimeSpan.FromSeconds(30));
                     batch.StringIncrementAsync("batch2", 2);
                 });

            //lock   
            Parallel.For(0, 10000, item =>
            {
                var is_success = client.ExcuteWithLock("lock_key", db => db.StringIncrement("lock", item));
            });

            //string
            var string_set_result = client.StringSet("string1", DateTime.Now, TimeSpan.FromSeconds(30));
            var string_get_result = client.StringGet<DateTime>("string1", TimeSpan.FromSeconds(30));

            //hash
            var hash_set_result = client.HashSet("hash1", new Dictionary<string, int> { ["key1"] = 123, ["2"] = 456, ["3"] = 123 }, TimeSpan.FromSeconds(30));
            var hash_get_result = client.HashGet<Dictionary<string, int>>("hash1", "key1", TimeSpan.FromSeconds(30));

            //list
            var list_enqueue = client.Enqueue("list1", 1);
            var list_dequeue = client.Dequeue<int>("list1");
            var list_page = client.ListGetPaged<int>("list1");

            //set
            var set_add_result = client.SetAdd("set1", "123");
            var set_get_result = client.SetGetList<string>("set1");

            //zset
            var zset_add_result = client.SortedSetAdd("zset1", 1, 1);
            var zset_rang_result = client.SortedSetByRankGetPaged<int>("zset1");
        }

        [Fact(DisplayName = "异步功能测试")]
        public async Task TestAsync()
        {
            var client = new RedisClientContext();


            var string_set_result1 = await client.StringSetAsync("key11", DateTime.Now, TimeSpan.FromSeconds(30));
            var string_get_result1 = await client.StringGetAsync<DateTime>("key11", TimeSpan.FromSeconds(30));

        }


        [Fact(DisplayName = "反射创建对象性能比较")]
        public void ReflectTest()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            //Method 1
            var dicType = typeof(Dictionary<,>).MakeGenericType(typeof(int), typeof(string));
            var dicInstance = Activator.CreateInstance(dicType, true);
            var addMethod = dicInstance.GetType().GetMethod("Add");

            for (int i = 0; i < 10000000; i++)
            {
                var param = new object[] { i, i.ToString() };
                addMethod.Invoke(dicInstance, param);
            }
            var reult2 = dicInstance as IDictionary<int, string>;

            ///Method 2 
            //var dicType = typeof(Dictionary<,>).MakeGenericType(typeof(int), typeof(string));
            //var newDictionaryExpression = Expression.New(dicType);
            //var addMethod = dicType.GetMethod("Add");

            //IList<ElementInit> elementInits = new List<ElementInit>();
            //for (int i = 0; i < 10000000; i++)
            //{
            //    elementInits.Add(Expression.ElementInit(addMethod, Expression.Constant(i), Expression.Constant(i.ToString())));
            //}
            //var listInitExpression = Expression.ListInit(newDictionaryExpression, elementInits);
            //Expression<Func<dynamic>> lambda = Expression.Lambda<Func<dynamic>>(listInitExpression);
            //var reult1 = lambda.Compile(true)() as IDictionary<int, string>;

            //Method 3
            //var dicType = typeof(Dictionary<,>).MakeGenericType(typeof(int), typeof(string));
            //var dicInstance = Activator.CreateInstance(dicType, true);
            //var paras = dicInstance.GetType().GetGenericArguments()?.Select(item => Expression.Parameter(item, item.Name))?.ToArray();
            //MethodCallExpression methodCall = Expression.Call(Expression.Constant(dicInstance), dicInstance.GetType().GetMethod("Add"), paras);

            //for (int i = 0; i < 10000000; i++)
            //{
            //    var param = new object[] { i, i.ToString() };
            //    Expression.Lambda(methodCall, paras).Compile().DynamicInvoke(param);
            //}
            //var reult3 = dicInstance as IDictionary<int, string>;


            stopwatch.Stop();
            var runtime = stopwatch.ElapsedMilliseconds;

            return;
        }
    }
}

