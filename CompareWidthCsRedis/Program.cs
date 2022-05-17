using CSRedis;
using SoftSunlight.RedisClient;
using System;
using System.Diagnostics;
using System.Text;

namespace CompareWidthCsRedis
{
    class Program
    {
        static void Main(string[] args)
        {
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
