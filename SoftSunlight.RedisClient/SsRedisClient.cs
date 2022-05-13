using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SoftSunlight.RedisClient
{
    /// <summary>
    /// Redis Client
    /// </summary>
    public class SsRedisClient
    {
        private string connectionStr;

        private TcpClient client;

        private AutoResetEvent autoResetEvent = new AutoResetEvent(false);

        private string responseData;

        #region 构造函数
        /// <summary>
        /// 默认连接127.0.0.1:6379服务端
        /// </summary>
        public SsRedisClient() : this("127.0.0.1:6379")
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="connectionStr">redis连接字符串，集群地址使用;分开</param>
        public SsRedisClient(string connectionStr)
        {
            this.connectionStr = connectionStr;
            string[] connStrArray = this.connectionStr.Split(',');
            string ipInfo = connStrArray[0];
            string[] ipAndPort = ipInfo.Split(':');
            string ip = ipAndPort[0];
            int port = 6379;
            if (ipAndPort.Length >= 2)
            {
                try
                {
                    port = Convert.ToInt32(ipAndPort[1]);
                }
                catch (Exception ex)
                {

                }
            }
            client = new TcpClient();
            client.Connect(ip, port);
            Task.Run(() =>
            {
                NetworkStream networkStream = client.GetStream();
                while (true)
                {
                    try
                    {
                        MemoryStream memoryStream = new MemoryStream();
                        do
                        {
                            byte[] buffer = new byte[64];
                            int realReadCount = networkStream.Read(buffer, 0, buffer.Length);
                            memoryStream.Write(buffer, 0, realReadCount);
                        } while (networkStream.DataAvailable);
                        responseData = string.Empty;
                        string responseStr = Encoding.UTF8.GetString(memoryStream.ToArray());
                        memoryStream.Close();
                        if (string.IsNullOrEmpty(responseStr))
                        {
                            break;
                        }
                        responseData = responseStr;
                        autoResetEvent.Set();
                    }
                    catch (Exception ex)
                    {

                    }
                }
            });
        }

        #endregion

        #region String Command
        /// <summary>
        /// 为指定的key追加value值，key存在就追加，不存在就设置为value值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns>追加指定值之后，key中字符串的长度</returns>
        public int Append(string key, string value)
        {
            string cmd = "append " + key + " " + value;
            string respData = RedisConverter.GetRespData(cmd);
            NetworkStream networkStream = client.GetStream();
            byte[] buffer = Encoding.UTF8.GetBytes(respData);
            networkStream.Write(buffer, 0, buffer.Length);
            autoResetEvent.WaitOne();
            string orignalData = responseData;
            if (orignalData.IndexOf("-") == 0)
            {
                throw new Exception(orignalData.Substring(1, orignalData.Length - 3));
            }
            return Convert.ToInt32(orignalData.Substring(1, orignalData.Length - 3));
        }

        /// <summary>
        /// 统计字符串被设置为1的位数
        /// </summary>
        /// <param name="key"></param>
        /// <returns>被设置为1的位的数量</returns>
        public int BitCount(string key)
        {
            return 0;
        }

        /// <summary>
        /// 统计字符串被设置为1的位数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns>被设置为1的位的数量</returns>
        public int BitCount(string key, int start, int end)
        {
            return 0;
        }

        #endregion

        #region Key Command
        /// <summary>
        /// 判断key是否存在
        /// </summary>
        /// <param name="key"></param>
        /// <returns>1 存在，0 不存在</returns>
        public bool Exists(string key)
        {
            string cmd = "exists " + key;
            string respData = RedisConverter.GetRespData(cmd);
            NetworkStream networkStream = client.GetStream();
            byte[] buffer = Encoding.UTF8.GetBytes(respData);
            networkStream.Write(buffer, 0, buffer.Length);
            autoResetEvent.WaitOne();
            string orignalData = responseData;
            if (orignalData.IndexOf("-") == 0)
            {
                throw new Exception(orignalData.Substring(1, orignalData.Length - 3));
            }
            int result = Convert.ToInt32(orignalData.Substring(1, orignalData.Length - 3));
            return result == 1 ? true : false;
        }
        #endregion

        #region Server Command
        public bool FlushDB()
        {
            string cmd = "flushdb";
            string respData = RedisConverter.GetRespData(cmd);
            NetworkStream networkStream = client.GetStream();
            byte[] buffer = Encoding.UTF8.GetBytes(respData);
            networkStream.Write(buffer, 0, buffer.Length);
            autoResetEvent.WaitOne();
            string orignalData = responseData;
            if (orignalData.IndexOf("-") == 0)
            {
                throw new Exception(orignalData.Substring(1, orignalData.Length - 3));
            }
            string result = orignalData.Substring(1, orignalData.Length - 3);
            return result == "OK" ? true : false;
        }
        #endregion

        public string Ping()
        {
            string cmd = "ping";
            string respData = RedisConverter.GetRespData(cmd);
            NetworkStream networkStream = client.GetStream();
            byte[] buffer = Encoding.UTF8.GetBytes(respData);
            networkStream.Write(buffer, 0, buffer.Length);
            autoResetEvent.WaitOne();
            string orignalData = responseData;
            if (orignalData.IndexOf("-") == 0)
            {
                throw new Exception(orignalData.Substring(1, orignalData.Length - 3));
            }
            return orignalData.Substring(1, orignalData.Length - 3);
        }
    }
}
