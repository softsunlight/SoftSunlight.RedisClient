using System;
using Xunit;

namespace SoftSunlight.RedisClient.Test
{
    /// <summary>
    /// ConnectionÃüÁîµ¥Ôª²âÊÔÀà
    /// </summary>
    public class ConnectionCommandTest
    {
        [Fact]
        public void PingTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            string result = ssRedisClient.Ping();
            Assert.Equal("PONG", result);
        }
    }
}
