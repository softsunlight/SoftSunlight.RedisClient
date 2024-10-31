using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SoftSunlight.RedisClient.Test
{
    /// <summary>
    /// Pub/Sub命令测试类
    /// </summary>
    public class PubSubCommandTest
    {
        [Fact]
        public void PSubscribeTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            string result = ssRedisClient.PSubscribe("testChannel");
            Assert.Equal("psubscribe testChannel 1", result);
        }

        [Fact]
        public void PublishTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            long result = ssRedisClient.Publish("testChannel", "aaa");
            Assert.Equal(1, result);
        }

        [Fact]
        public void PublishTest2()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("159.138.237.10:33619");
            string authResult = ssRedisClient.Auth("7ASvV69tqnkUR5MygBNt");
            long result = ssRedisClient.Publish("testChannel", "aaa");
            Assert.Equal(1, result);
        }

        [Fact]
        public void SubscribeTest()
        {
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            string result = ssRedisClient.Subscribe("testChannel");
            Assert.Equal("subscribe testChannel 1", result);
        }
    }
}
