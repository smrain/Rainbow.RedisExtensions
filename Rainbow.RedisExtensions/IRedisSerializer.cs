using System;

namespace Rainbow.RedisExtensions
{
    public interface IRedisSerializer
    {
        string Serialize(object obj);

        TResult Deserialize<TResult>(string obj);
        object Deserialize(string obj, Type resultType);
    }
}
