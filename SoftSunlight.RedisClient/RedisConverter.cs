using Newtonsoft.Json;
using SoftSunlight.RedisClient.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SoftSunlight.RedisClient
{
    /// <summary>
    /// 将RedisCommand类转换为RESP协议格式数据
    /// </summary>
    class RedisConverter
    {
        /// <summary>
        /// 将redis命令转换为RESP格式的字符串
        /// </summary>
        /// <param name="redisCommand"></param>
        /// <returns></returns>
        public static string GetRespData(RedisCommand redisCommand)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("*").Append(1 + (redisCommand.Params != null ? redisCommand.Params.Count : 0)).Append("\r\n");
            string cmdName = "";
            Type enumType = redisCommand.RedisCommands.GetType();
            var currentEnumField = enumType.GetFields().FirstOrDefault(p => p.Name == redisCommand.RedisCommands.ToString());
            if (currentEnumField == null)
            {
                throw new Exception("unknown command name");
            }
            var attrs = currentEnumField.GetCustomAttributes(typeof(CommandAliasAttribute), false);
            if (attrs != null && attrs.Length > 0)
            {
                var obj = attrs[0];
                Type attrType = typeof(CommandAliasAttribute);
                cmdName = attrType.GetProperty("Name").GetValue(obj)?.ToString();
            }
            else
            {
                cmdName = redisCommand.RedisCommands.ToString();
            }
            stringBuilder.Append("$").Append(cmdName.Length).Append("\r\n").Append(cmdName).Append("\r\n");
            if (redisCommand.Params != null && redisCommand.Params.Count > 0)
            {
                foreach (var item in redisCommand.Params)
                {
                    string itemStr = GetObjectStringValue(item);
                    byte[] data = Encoding.UTF8.GetBytes(itemStr);
                    stringBuilder.Append("$").Append(data.Length).Append("\r\n").Append(itemStr).Append("\r\n");
                }
            }
            return stringBuilder.ToString();
        }

        /// <summary>
        /// 将redis命令转换为RESP格式的字符串(批量)
        /// </summary>
        /// <param name="redisCommands"></param>
        /// <returns></returns>
        public static string GetRespData(IList<RedisCommand> redisCommands)
        {
            StringBuilder stringBuilder = new StringBuilder();
            if (redisCommands != null && redisCommands.Count > 0)
            {
                foreach (var item in redisCommands)
                {
                    stringBuilder.Append(GetRespData(item));
                }
            }
            return stringBuilder.ToString();
        }

        /// <summary>
        /// 序列化命令参数
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private static string GetObjectStringValue(object obj)
        {
            Type type = obj.GetType();
            string itemStr = string.Empty;
            if (obj is byte[])
            {
                itemStr = Encoding.UTF8.GetString((byte[])obj);
            }
            if (obj is string)
            {
                itemStr = obj.ToString();
            }
            else if (type.IsClass)
            {
                itemStr = JsonConvert.SerializeObject(obj);
            }
            else if (type.IsArray)
            {
                var count = Convert.ToInt32(type.GetProperty("Count")?.GetValue(obj));
                var itemProperty = type.GetProperty("Item");//索引器属性
                List<object> list = new List<object>();
                for (var i = 1; i <= count; i++)
                {
                    var tempObj = itemProperty.GetValue(obj, new object[] { i - 1 });
                    list.Add(tempObj);
                }
                itemStr = "[" + string.Join(",", list) + "]";
            }
            else
            {
                itemStr = obj.ToString();
            }
            return itemStr;
        }

    }
}
