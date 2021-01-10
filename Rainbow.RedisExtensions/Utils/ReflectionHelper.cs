using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Rainbow.RedisExtensions.Utils
{
    internal static class ReflectionHelper
    {
        private static readonly Lazy<Type[]> SimpleTypesInternal = new Lazy<Type[]>(() =>
        {
            var types = new[]
            {
                typeof (Enum),
                typeof (string),
                typeof (char),
                typeof (Guid),

                typeof (bool),
                typeof (byte),
                typeof (short),
                typeof (int),
                typeof (long),
                typeof (float),
                typeof (double),
                typeof (decimal),

                typeof (sbyte),
                typeof (ushort),
                typeof (uint),
                typeof (ulong),

                typeof (DateTime),
                typeof (DateTimeOffset),
                typeof (TimeSpan),
            };

            var nullableTypes = from t in types
                                where t != typeof(Enum) && t != typeof(string)
                                select typeof(Nullable<>).MakeGenericType(t);
            types.Append(typeof(byte[]));
            return types.Concat(nullableTypes).ToArray();
        });

        private static readonly Type[] RedisValueTypes = new Type[]
        {
             typeof(uint),
             typeof(int),
             typeof(ulong),
             typeof(long),
             typeof(bool),
             typeof(byte[]),
             typeof(string),
             typeof(Memory<byte>),
             typeof(ReadOnlyMemory<byte>),
             typeof(double?),
             typeof(double),
             typeof(uint?),
            typeof(int?),
             typeof(ulong?),
             typeof(long?),
             typeof(bool?),
        };

        public static bool IsSimpleType(this Type src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            return src.GetTypeInfo().IsEnum ||
                (src.GetTypeInfo().IsGenericType &&
                    src.GetTypeInfo().GetGenericTypeDefinition() == typeof(Nullable<>) &&
                    src.GetTypeInfo().GetGenericArguments().First().GetTypeInfo().IsEnum) ||
                SimpleTypesInternal.Value.Contains(src);
        }

        public static bool IsRedisValueType(this Type src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            return RedisValueTypes.Contains(src);
        }

        public static MemberInfo GetProperty(LambdaExpression lambda)
        {
            Expression expr = lambda;
            for (; ; )
            {
                switch (expr.NodeType)
                {
                    case ExpressionType.Lambda:
                        expr = ((LambdaExpression)expr).Body;
                        break;
                    case ExpressionType.Convert:
                        expr = ((UnaryExpression)expr).Operand;
                        break;
                    case ExpressionType.MemberAccess:
                        MemberExpression memberExpression = (MemberExpression)expr;
                        MemberInfo mi = memberExpression.Member;
                        return mi;
                    default:
                        return null;
                }
            }
        }

        public static IDictionary<string, object> GetObjectValues(object obj)
        {
            IDictionary<string, object> result = new Dictionary<string, object>();
            if (obj == null) return result;

            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public;
            foreach (var propertyInfo in obj.GetType().GetProperties(bindingFlags))
            {
                string name = propertyInfo.Name;
                object value = propertyInfo.GetValue(obj, null);
                result[name] = value;
            }

            return result;
        }

        public static bool TryChangeType(object obj, Type conversionType, out object value)
        {
            if (conversionType.IsGenericType && conversionType.GetGenericTypeDefinition().Equals(typeof(Nullable<>)))
            {
                if (obj == null) { value = null; return true; }
                var nullableConverter = new System.ComponentModel.NullableConverter(conversionType);
                conversionType = nullableConverter.UnderlyingType;
            }
            try
            {
                value = Convert.ChangeType(obj, conversionType);
                return true;
            }
            catch
            {
                value = obj;
                return false;
            }
        }

    }
}