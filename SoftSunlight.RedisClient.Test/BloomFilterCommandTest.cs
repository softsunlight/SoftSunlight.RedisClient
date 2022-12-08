using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SoftSunlight.RedisClient.Test
{
    /// <summary>
    /// 布隆过滤器命令单元测试类
    /// </summary>
    public class BloomFilterCommandTest
    {
        [Fact]
        public void BFExistsTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("120.46.160.122:6379");
            string key = "aaa";
            bool b = ssRedisClient.BFExists(key, "aa");
            Assert.False(b);
        }

        [Fact]
        public void BFAddTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("120.46.160.122:6379");
            string key = "aaa";
            bool b = ssRedisClient.BFAdd(key, "aa");
            Assert.True(b);
            ssRedisClient.Del(key);
        }
    }
}
