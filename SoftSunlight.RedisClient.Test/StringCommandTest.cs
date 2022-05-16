using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SoftSunlight.RedisClient.Test
{
    /// <summary>
    /// 字符串命令单元测试类
    /// </summary>
    public class StringCommandTest
    {
        [Fact]
        public void AppendTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            ssRedisClient.FlushDB();
            long existsResult = ssRedisClient.Exists("aaa");
            Assert.Equal(0, existsResult);
            long result = ssRedisClient.Append("aaa", "value1");
        }

        [Fact]
        public void BitCountTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            ssRedisClient.FlushDB();
            long result = ssRedisClient.BitCount("aaa");
            Assert.Equal(0, result);
            result = ssRedisClient.BitCount("aaa", 0, -1);
            Assert.Equal(0, result);
        }

        [Fact]
        public void GetTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            ssRedisClient.FlushDB();
            string result = ssRedisClient.Get("aaa");
            Assert.Null(result);
        }

    }
}
