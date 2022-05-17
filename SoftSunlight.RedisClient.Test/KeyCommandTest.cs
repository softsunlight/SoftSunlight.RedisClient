using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SoftSunlight.RedisClient.Test
{
    public class KeyCommandTest
    {
        [Fact]
        public void DelTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            ssRedisClient.FlushDB();
            long result = ssRedisClient.Del("aaa");
            Assert.Equal(0, result);
            ssRedisClient.Set("aaa", "aaa");
            result = ssRedisClient.Del("aaa");
            Assert.Equal(1, result);
        }

        [Fact]
        public void DumpTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            ssRedisClient.FlushDB();
            ssRedisClient.Set("aaa", "aaa");
            string result = ssRedisClient.Dump("aaa");
            Assert.NotNull(result);
        }
    }
}
