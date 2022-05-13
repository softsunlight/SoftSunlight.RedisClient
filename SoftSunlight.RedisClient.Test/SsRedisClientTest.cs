using System;
using Xunit;

namespace SoftSunlight.RedisClient.Test
{
    public class SsRedisClientTest
    {
        [Fact]
        public void PingTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            string result = ssRedisClient.Ping();
            Assert.Equal("PONG", result);
        }

        [Fact]
        public void StringCommandTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            ssRedisClient.FlushDB();
            bool flag = ssRedisClient.Exists("aaa");
            Assert.False(flag);
            int result = ssRedisClient.Append("aaa", "value1");
            Assert.Equal("value1".Length, result);
        }
    }
}
