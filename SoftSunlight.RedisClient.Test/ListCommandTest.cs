using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SoftSunlight.RedisClient.Test
{
    /// <summary>
    /// List命令单元测试类
    /// </summary>
    public class ListCommandTest
    {
        [Fact]
        public void LPushTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("120.46.160.122:6379");
            string key = "test_list";
            string value = "http://www.baidu.com";
            long pushResult = ssRedisClient.LPush(key, value);
            Assert.Equal(1, pushResult);
            string popValue = ssRedisClient.RPop(key);
            Assert.Equal(popValue, value);
        }
    }
}
