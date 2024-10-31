using CSRedis;
using SoftSunlight.RedisClient;
using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Text;

namespace CompareWidthCsRedis
{
    class Program
    {
        static void Main(string[] args)
        {
            //CSRedisClient csRedisClient = new CSRedisClient("159.138.237.10:33619,password=7ASvV69tqnkUR5MygBNt,poolsize=5,defaultDatabase=0,ssl=false,writeBuffer=10240,testcluster=false");
            //long r = csRedisClient.Publish("test", "test");

            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("159.138.237.10:33619,defaultDatabase=0,password=7ASvV69tqnkUR5MygBNt");
            var db = redis.GetDatabase(0);
            long r = db.Publish("testChannel", "test");

            //CSRedisClient csRedisClient = new CSRedisClient("127.0.0.1:6379");
            //csRedisClient.Publish("test", "test");

            //Stopwatch stopwatch = new Stopwatch();
            //stopwatch.Start();
            SsRedisClient ssRedisClient = new SsRedisClient("192.168.10.131:6379");
            ssRedisClient.Subscribe("testChannel");
            ssRedisClient.Subscribed += (e) =>
            {
                Console.WriteLine(e.Message);
            };
            Console.Read();
            //for (int i = 0; i < 20000; i++)
            //{
            //    ssRedisClient.Append("aa", i.ToString());
            //}
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed.TotalMilliseconds);
            //stopwatch.Restart();
            //CSRedisClient client = new CSRedisClient("192.168.10.131:6379");
            //for (int i = 0; i < 20000; i++)
            //{
            //    client.Append("aa", i);
            //}
            //stopwatch.Stop();
            //Console.WriteLine(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

}
