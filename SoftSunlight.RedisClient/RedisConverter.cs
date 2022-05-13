using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient
{
    /// <summary>
    /// 将RedisCommand类转换为RESP协议格式数据
    /// </summary>
    class RedisConverter
    {
        public static string GetRespData(string cmd)
        {
            StringBuilder stringBuilder = new StringBuilder();
            string[] cmdArr = cmd.Split(' ');
            stringBuilder.Append("*").Append(cmdArr.Length).Append("\r\n");
            foreach (var item in cmdArr)
            {
                stringBuilder.Append("$").Append(item.Length).Append("\r\n").Append(item).Append("\r\n");
            }
            return stringBuilder.ToString();
        }
    }
}
