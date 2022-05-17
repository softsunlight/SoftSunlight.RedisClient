using SoftSunlight.RedisClient.Enum;
using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Redis连接字符串
        /// </summary>
        private string connectionStr;
        /// <summary>
        /// Redis Tcp Client
        /// </summary>
        private TcpClient client;
        /// <summary>
        /// request/response同步事件
        /// </summary>
        private AutoResetEvent autoResetEvent = new AutoResetEvent(false);
        /// <summary>
        /// 响应数据
        /// </summary>
        private string responseData;
        /// <summary>
        /// Redis集群地址
        /// </summary>
        private string[] servers;
        /// <summary>
        /// 当前连接的服务器索引号
        /// </summary>
        private int connServerIndex;
        /// <summary>
        /// 是否有接收数据的线程
        /// </summary>
        private bool hasReviceThread;

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
            this.servers = this.connectionStr.Split(';');
            this.client = new TcpClient();
            //连接成功则接收数据
            BuildConnection();
        }

        #endregion

        #region redis连接管理
        /// <summary>
        /// 建立连接
        /// </summary>
        private void BuildConnection()
        {
            bool isConnSuccess = false;
            for (int i = this.connServerIndex; i < this.connServerIndex + this.servers.Length; i++)
            {
                int realIndex = i % this.servers.Length;
                string[] connStrArray = this.servers[realIndex].Split(',');
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
                int maxCount = 3;
                int count = 0;
                do
                {
                    if (isConnSuccess = Connection(ip, port))
                    {
                        this.connServerIndex = realIndex;
                        break;
                    }
                    Thread.Sleep(50);
                } while (++count < maxCount);
            }
            if (!isConnSuccess)
            {
                throw new Exception("all servers is not useable");
            }
            ReceiveData();
        }
        /// <summary>
        /// 发送redis命令给服务端
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="redisCommand"></param>
        /// <returns></returns>
        private T SendCommand<T>(RedisCommand redisCommand)
        {
            string respData = RedisConverter.GetRespData(redisCommand);
            NetworkStream networkStream = client.GetStream();
            byte[] buffer = Encoding.UTF8.GetBytes(respData);
            try
            {
                networkStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                //失败则重新连接一次试试
                BuildConnection();
                networkStream.Write(buffer, 0, buffer.Length);
            }
            autoResetEvent.WaitOne();
            string orignalData = responseData;
            if (orignalData.IndexOf("-") == 0)//redis错误信息
            {
                throw new Exception(orignalData.Substring(1, orignalData.Length - 3));
            }
            else if (orignalData.IndexOf("+") == 0)//单行字符串
            {
                object obj = string.Empty;
                obj = orignalData.Substring(1, orignalData.Length - 3);
                return (T)obj;
            }
            else if (orignalData.IndexOf(":") == 0)//整数
            {
                var obj = Activator.CreateInstance(typeof(T));
                obj = Convert.ToInt64(orignalData.Substring(1, orignalData.Length - 3));
                return (T)obj;
            }
            else if (orignalData.IndexOf("$") == 0)//多行字符串
            {
                object obj = string.Empty;
                int count = 0;
                int validCharIndex = 0;
                for (var i = 1; i < orignalData.Length; i++)
                {
                    if (orignalData[i] == '\r')
                    {
                        count = Convert.ToInt32(orignalData.Substring(1, i - 1));
                        validCharIndex = i + 2;
                        break;
                    }
                }
                if (count == -1)
                {
                    return default(T);
                }
                obj = orignalData.Substring(validCharIndex, count);
                return (T)obj;
            }
            else if (orignalData.IndexOf("*") == 0)//数组
            {
                //订阅："*3\r\n$7\r\nmessage\r\n$11\r\ntestChannel\r\n$2\r\n11\r\n"
                object obj = null;
                List<string> list = new List<string>();
                int spaceIndex = orignalData.IndexOf('\r');
                int arrayLength = Convert.ToInt32(orignalData.Substring(1, spaceIndex - 1));
                orignalData = orignalData.Substring(spaceIndex + 2);
                int index = 0;
                do
                {
                    if (orignalData.IndexOf("$") == 0)
                    {
                        spaceIndex = orignalData.IndexOf('\r');
                        int count = Convert.ToInt32(orignalData.Substring(1, spaceIndex - 1));
                        list.Add(orignalData.Substring(spaceIndex + 2, count));
                        orignalData = orignalData.Substring(spaceIndex + 2 + count + 2);
                    }
                    else if (orignalData.IndexOf(":") == 0)
                    {
                        list.Add(orignalData.Substring(1, orignalData.Length - 3));
                    }
                } while (++index < arrayLength);
                obj = string.Join(" ", list);
                return (T)obj;
            }
            return default(T);
        }
        #endregion

        #region TCP Manage
        /// <summary>
        /// socket链接
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        private bool Connection(string ip, int port)
        {
            try
            {
                client.Connect(ip, port);
            }
            catch (Exception ex)
            {
                return false;
            }
            return true;
        }
        /// <summary>
        /// 接收数据
        /// </summary>
        private void ReceiveData()
        {
            if (!hasReviceThread)
            {
                Task.Run(() =>
                {
                    this.hasReviceThread = true;
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
                            if (responseData.Contains("\r\nmessage\r\n"))//订阅消息
                            {
                                string orignalData = responseData;
                                List<string> list = new List<string>();
                                int spaceIndex = orignalData.IndexOf('\r');
                                int arrayLength = Convert.ToInt32(orignalData.Substring(1, spaceIndex - 1));
                                orignalData = orignalData.Substring(spaceIndex + 2);
                                int index = 0;
                                do
                                {
                                    if (orignalData.IndexOf("$") == 0)
                                    {
                                        spaceIndex = orignalData.IndexOf('\r');
                                        int count = Convert.ToInt32(orignalData.Substring(1, spaceIndex - 1));
                                        list.Add(orignalData.Substring(spaceIndex + 2, count));
                                        orignalData = orignalData.Substring(spaceIndex + 2 + count + 2);
                                    }
                                    else if (orignalData.IndexOf(":") == 0)
                                    {
                                        list.Add(orignalData.Substring(1, orignalData.Length - 3));
                                    }
                                } while (++index < arrayLength);
                                if (this.Subscribed != null)
                                {
                                    this.Subscribed(new SubscribeEventArgs() { Channel = list[1], Message = list[2] });
                                }
                            }
                            else
                            {
                                autoResetEvent.Set();
                            }
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    this.hasReviceThread = false;
                });
            }
        }
        #endregion

        #region String Command
        /// <summary>
        /// 为指定的key追加value值，key存在就追加，不存在就设置为value值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value">字符串|数字|byte[]|Class对象</param>
        /// <returns>追加指定值之后，key中字符串的长度</returns>
        public long Append(string key, string value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Append;
            redisCommand.Params = new object[] { key, value };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 统计字符串被设置为1的位数
        /// </summary>
        /// <param name="key"></param>
        /// <returns>被设置为1的位的数量</returns>
        public long BitCount(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BitCount;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 统计字符串被设置为1的位数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns>被设置为1的位的数量</returns>
        public long BitCount(string key, int start, int end)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BitCount;
            redisCommand.Params = new object[] { key, start, end };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 对一个或多个保存二进制位的字符串key进行位元操作，并将结果保存到destKey上
        /// </summary>
        /// <param name="bitOpEnum"></param>
        /// <param name="destKey"></param>
        /// <param name="keys"></param>
        /// <returns>保存到destKey的字符串的长度，key中最长的字符串的长度</returns>
        public long BitOp(BitOpEnum bitOpEnum, string destKey, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BitOp;
            redisCommand.Params = new List<object> { bitOpEnum, destKey };
            if (keys != null && keys.Length > 0)
            {
                foreach (var item in keys)
                {
                    redisCommand.Params.Add(item);
                }
            }
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回字符串里面第一个被设置为1或者0的bit位
        /// </summary>
        /// <param name="key"></param>
        /// <param name="bit">0或者1</param>
        /// <returns>返回字符串中第一个被设置为1或者0的bit位，在空字符串中查找bit为1的内容，返回-1</returns>
        public long BitPos(string key, int bit)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BitPos;
            redisCommand.Params = new List<object> { key, bit };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回字符串里面第一个被设置为1或者0的bit位
        /// </summary>
        /// <param name="key"></param>
        /// <param name="bit">0或者1</param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns>返回字符串中第一个被设置为1或者0的bit位，在空字符串中查找bit为1的内容，返回-1</returns>
        public long BitPos(string key, int bit, int start, int end)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BitPos;
            redisCommand.Params = new List<object> { key, bit, start, end };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 为key存储的数字值减1
        /// </summary>
        /// <param name="key"></param>
        /// <returns>执行操作之后key中的值</returns>
        public long Decr(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Decr;
            redisCommand.Params = new List<object> { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 为key存储的数字值减decrement
        /// </summary>
        /// <param name="key"></param>
        /// <returns>执行操作之后key中的值</returns>
        public long DecrBy(string key, long decrement)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.DecrBy;
            redisCommand.Params = new List<object> { key, decrement };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回与键key相关联的字符串值
        /// </summary>
        /// <param name="key"></param>
        /// <returns>返回key中存储的值，不存在则返回null</returns>
        public string Get(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Get;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 获取指定偏移量上的位(bit)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public long GetBit(string key, int offset)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.GetBit;
            redisCommand.Params = new object[] { key, offset };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回key中的字符串的子串
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public string GetRange(string key, int start, int end)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.GetRange;
            redisCommand.Params = new object[] { key, start, end };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 将键key的值设为value,并返回旧的值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public string GetSet(string key, object value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.GetSet;
            redisCommand.Params = new object[] { key, value };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 为key存储的数字值加1
        /// </summary>
        /// <param name="key"></param>
        /// <returns>执行操作之后key中的值</returns>
        public long Incr(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Incr;
            redisCommand.Params = new List<object> { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 为key存储的数字值加decrement
        /// </summary>
        /// <param name="key"></param>
        /// <returns>执行操作之后key中的值</returns>
        public long IncrBy(string key, long decrement)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.IncrBy;
            redisCommand.Params = new List<object> { key, decrement };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 为键key中存储的值加上浮点数增量increment
        /// </summary>
        /// <param name="key"></param>
        /// <param name="increment"></param>
        /// <returns></returns>
        public string IncrByFloat(string key, float increment)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.IncrByFloat;
            redisCommand.Params = new List<object> { key, increment };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 获取给定key的值，值的类型为字符串，如果给定的key中某个不存在或者值不是字符串，则返回null
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IList<string> MGet(params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.MGet;
            redisCommand.Params = new List<object> { keys };
            return SendCommand<IList<string>>(redisCommand);
        }

        /// <summary>
        /// 批量设置多个key及各自的value
        /// </summary>
        /// <param name="keyValuePairs"></param>
        /// <returns></returns>
        public bool MSet(Dictionary<string, object> keyValuePairs)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.MSet;
            redisCommand.Params = new List<object>();
            if (keyValuePairs != null)
            {
                foreach (var keyValue in keyValuePairs)
                {
                    redisCommand.Params.Add(keyValue.Key);
                    redisCommand.Params.Add(keyValue.Value);
                }
            }
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 当且仅当所有给定键都不存在时，为所有给定键设置值
        /// </summary>
        /// <param name="keyValuePairs"></param>
        /// <returns>1：成功，0：失败</returns>
        public long MSetNx(Dictionary<string, object> keyValuePairs)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.MSetNx;
            redisCommand.Params = new List<object>();
            if (keyValuePairs != null)
            {
                foreach (var keyValue in keyValuePairs)
                {
                    redisCommand.Params.Add(keyValue.Key);
                    redisCommand.Params.Add(keyValue.Value);
                }
            }
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 以毫秒为单位设置key的生存时间
        /// </summary>
        /// <param name="key"></param>
        /// <param name="milliseconds"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool PSetEx(string key, long milliseconds, object value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.PSetEx;
            redisCommand.Params = new List<object> { key, milliseconds, value };
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 将键ke设置为指定的"字符串"值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value">字符串|数字|byte[]|Class对象</param>
        /// <returns></returns>
        public string Set(string key, object value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Set;
            redisCommand.Params = new object[] { key, value };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 将键ke设置为指定的"字符串"值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value">字符串|数字|byte[]|Class对象</param>
        /// <param name="seconds">过期时间(秒)</param>
        /// <returns></returns>
        public string Set(string key, object value, int seconds)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Set;
            redisCommand.Params = new object[] { key, value, seconds };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 将键ke设置为指定的"字符串"值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value">字符串|数字|byte[]|Class对象</param>
        /// <param name="seconds">过期时间(秒)</param>
        /// <param name="setOption">键设置选项</param>
        /// <returns></returns>
        public string Set(string key, object value, int seconds, SetOptionEnum setOption)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Set;
            redisCommand.Params = new object[] { key, value, seconds, setOption };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 将键ke设置为指定的"字符串"值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value">字符串|数字|byte[]|Class对象</param>
        /// <param name="milliseconds">过期时间(毫秒)</param>
        /// <returns></returns>
        public string Set(string key, object value, long milliseconds)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Set;
            redisCommand.Params = new object[] { key, value, milliseconds };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 将键ke设置为指定的"字符串"值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value">字符串|数字|byte[]|Class对象</param>
        /// <param name="milliseconds">过期时间(毫秒)</param>
        /// <param name="setOption">键设置选项</param>
        /// <returns></returns>
        public string Set(string key, object value, long milliseconds, SetOptionEnum setOption)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Set;
            redisCommand.Params = new object[] { key, value, milliseconds, setOption };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 设置或清除指定偏移量上的位(bit)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="offset"></param>
        /// <param name="value"></param>
        /// <returns>存储在offset偏移位的原始值</returns>
        public long SetBit(string key, int offset, bool value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SetBit;
            redisCommand.Params = new object[] { key, offset, (value ? 1 : 0) };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 将键设置为value,并指定seconds秒生存时间
        /// </summary>
        /// <param name="key"></param>
        /// <param name="seconds"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool SetEx(string key, int seconds, object value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SetEx;
            redisCommand.Params = new object[] { key, seconds, value };
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// key不存在时设置value,存在时什么也不做
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns>1：设置成功，0：设置失败</returns>
        public long SetNx(string key, object value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SetNx;
            redisCommand.Params = new object[] { key, value };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 从偏移量offset开始，用value参数覆盖key存储的字符串值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="offset"></param>
        /// <param name="value"></param>
        /// <returns>返回被修改后的字符串值的长度</returns>
        public long SetRange(string key, int offset, object value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SetRange;
            redisCommand.Params = new object[] { key, offset, value };
            return SendCommand<long>(redisCommand);
        }

        ///// <summary>
        ///// 实现基于字符串的复杂算法，目前唯一实现为LCS(最长公共子序列)算法
        ///// </summary>
        ///// <param name="algoEnum"></param>
        ///// <returns></returns>
        //public string StrAlgo(AlgoEnum algoEnum)
        //{

        //}

        /// <summary>
        /// 返回Key所存储的字符串的长度
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public long StrLen(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.StrLen;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        #endregion

        #region Hash Command
        /// <summary>
        /// 删除哈希表key中的一个或者多个指定字段
        /// </summary>
        /// <param name="key"></param>
        /// <param name="fields"></param>
        /// <returns>被成功删除字段的数量</returns>
        public long HDel(string key, params string[] fields)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HDel;
            redisCommand.Params = new object[] { key, fields };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 判断哈希表中是否存在指定字段field
        /// </summary>
        /// <param name="key"></param>
        /// <param name="field"></param>
        /// <returns>1：有field，0：key不存在或不存在field</returns>
        public long HExists(string key, string field)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HExists;
            redisCommand.Params = new object[] { key, field };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回哈希表中指定字段的值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="field"></param>
        /// <returns></returns>
        public object HGet(string key, string field)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HGet;
            redisCommand.Params = new object[] { key, field };
            return SendCommand<object>(redisCommand);
        }

        /// <summary>
        /// 返回哈希表中所有的域和值
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IList<object> HGetAll(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HGetAll;
            redisCommand.Params = new object[] { key };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 为哈希表key中的field的值加上增量increment
        /// </summary>
        /// <param name="key"></param>
        /// <param name="field"></param>
        /// <param name="increment"></param>
        /// <returns>执行命令后，哈希表中域field的值</returns>
        public long HIncrBy(string key, string field, int increment)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HIncrBy;
            redisCommand.Params = new object[] { key, field, increment };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 为哈希表key中的field的值加上增量increment
        /// </summary>
        /// <param name="key"></param>
        /// <param name="field"></param>
        /// <param name="increment"></param>
        /// <returns>执行命令后，哈希表中域field的值</returns>
        public string HIncrByFloat(string key, string field, float increment)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HIncrByFloat;
            redisCommand.Params = new object[] { key, field, increment };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 返回哈希表中所有的域
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IList<object> HKeys(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HKeys;
            redisCommand.Params = new object[] { key };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 返回哈希表中字段的数量
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public long HLen(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HLen;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回哈希表中一个或多个给定字段的值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public IList<object> HMGet(string key, params string[] fields)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HMGet;
            redisCommand.Params = new object[] { key, fields };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 设置多个字段值到对应的哈希表中
        /// </summary>
        /// <param name="key"></param>
        /// <param name="keyValuePairs"></param>
        /// <returns></returns>
        [Obsolete("please use HSet command replace this")]
        public string HMSet(string key, Dictionary<string, object> keyValuePairs)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HMSet;
            redisCommand.Params = new object[] { key };
            if (keyValuePairs != null)
            {
                foreach (var keyValue in keyValuePairs)
                {
                    redisCommand.Params.Add(keyValue.Key);
                    redisCommand.Params.Add(keyValue.Value);
                }
            }
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 遍历哈希表中的键值对
        /// </summary>
        /// <param name="key"></param>
        /// <param name="cursor"></param>
        /// <returns></returns>
        public string HScan(string key, int cursor)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HScan;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 设置多个字段值到对应的哈希表中
        /// </summary>
        /// <param name="key"></param>
        /// <param name="keyValuePairs"></param>
        /// <returns>被修改或增加的field的个数</returns>
        public long HSet(string key, Dictionary<string, object> keyValuePairs)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HSet;
            redisCommand.Params = new object[] { key };
            if (keyValuePairs != null)
            {
                foreach (var keyValue in keyValuePairs)
                {
                    redisCommand.Params.Add(keyValue.Key);
                    redisCommand.Params.Add(keyValue.Value);
                }
            }
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 为哈希表中不存在的字段赋值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="keyValuePairs"></param>
        /// <returns>1：设置成功，0：field已存在，设置失败</returns>
        public long HSetNx(string key, string field, object value)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HSetNx;
            redisCommand.Params = new object[] { key, field, value };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回field的字符串长度
        /// </summary>
        /// <param name="key"></param>
        /// <param name="field"></param>
        /// <returns>字符串长度，key或field不存在则返回0</returns>
        public long HStrLen(string key, string field)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HStrLen;
            redisCommand.Params = new object[] { key, field };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回哈希表中所有域(field)的值
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IList<object> HVals(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.HVals;
            redisCommand.Params = new object[] { key };
            return SendCommand<IList<object>>(redisCommand);
        }

        #endregion

        #region List Command
        //public string BLMove(string source, string destination)
        //{

        //}

        /// <summary>
        /// 移除并获取列表的第一个元素，列表为空则阻塞列表直到超时或者发现可弹出元素为止
        /// </summary>
        /// <param name="timeout"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IList<object> BLPop(int timeout, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BLPop;
            redisCommand.Params = new object[] { keys, timeout };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 从给的列表参数中按顺序检查第一个不为空的列表，然后从该列表的尾部移除元素，列表为空则阻塞列表直到超时或者发现可弹出元素为止
        /// </summary>
        /// <param name="timeout"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IList<object> BRPop(int timeout, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BRPop;
            redisCommand.Params = new object[] { keys, timeout };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 从列表中取出最后一个元素，插入到另外一个列表的头部，列表没有元素会阻塞直到等待超时或发现可弹出元素为止
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public string BRPopLPush(string source, string destination, int timeout)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BRPopLPush;
            redisCommand.Params = new object[] { source, destination, timeout };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 返回列表key中索引Index位存储的元素，index从0开始
        /// </summary>
        /// <param name="key"></param>
        /// <param name="index"></param>
        /// <returns>查询的元素，索引超出范围返回NULL</returns>
        public string LIndex(string key, int index)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LIndex;
            redisCommand.Params = new object[] { key, index };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 把element插入到列表中参考值pivot的前面或者后面
        /// </summary>
        /// <param name="key"></param>
        /// <param name="insertOptionEnum"></param>
        /// <param name="pivot"></param>
        /// <param name="element"></param>
        /// <returns>执行操作后的列表长度，pivot参考值不存在的时候返回-1</returns>
        public long LInsert(string key, InsertOptionEnum insertOptionEnum, int pivot, object element)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LIndex;
            redisCommand.Params = new object[] { key, insertOptionEnum, pivot, element };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回存储在key中的列表长度
        /// </summary>
        /// <param name="key"></param>
        /// <returns>列表的长度</returns>
        public long LLen(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LLen;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 原子的从列表source中移除并返回第一个或者最后一个元素，然后把这个元素插入到列表destination的第一个或最后一个元素
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="moveOptionEnumFirst"></param>
        /// <param name="moveOptionEnumSecond"></param>
        /// <returns></returns>
        public string LMove(string source, string destination, MoveOptionEnum moveOptionEnumFirst, MoveOptionEnum moveOptionEnumSecond)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LMove;
            redisCommand.Params = new object[] { source, destination, moveOptionEnumFirst, moveOptionEnumSecond };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 删除并返回存储在Key中的第一个元素
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string LPop(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LPop;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 返回列表中匹配给定element成员的索引
        /// </summary>
        /// <param name="key"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public object LPos(string key, object element)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LPos;
            redisCommand.Params = new object[] { key, element };
            return SendCommand<object>(redisCommand);
        }

        /// <summary>
        /// 将多个值插入到列表的头部
        /// </summary>
        /// <param name="key"></param>
        /// <param name="elements"></param>
        /// <returns></returns>
        public long LPush(string key, params string[] elements)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LPush;
            redisCommand.Params = new object[] { key, elements };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// key存在并且存储这一个list类型值时，向list的头部多个值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="elements"></param>
        /// <returns></returns>
        public long LPushX(string key, params string[] elements)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LPushX;
            redisCommand.Params = new object[] { key, elements };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回列表中指定区间内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public IList<object> LRange(string key, int start, int end)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LRange;
            redisCommand.Params = new object[] { key, start, end };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 从列表中删除前count个值等于element的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="count">>0：从头到尾删除,<0：从尾到头删除值,=0：移除所有为element的元素</param>
        /// <param name="element"></param>
        /// <returns>删除元素的个数</returns>
        public long LRem(string key, int count, object element)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LRem;
            redisCommand.Params = new object[] { key, count, element };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 设置列表中index的元素值为element
        /// </summary>
        /// <param name="key"></param>
        /// <param name="index"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        public string LSet(string key, int index, object element)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LSet;
            redisCommand.Params = new object[] { key, index, element };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 修剪一个已存在的list，这样list就只包含指定范围的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public string LTrim(string key, int start, int end)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.LTrim;
            redisCommand.Params = new object[] { key, start, end };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 移除并返回列表最后一个元素
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string RPop(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.RPop;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 原子的从source众移除并返回最后一个元素，然后把这个元素插入到destination的第一个元素
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <returns>移除并又插入的元素</returns>
        public string RPopLPush(string source, string destination)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.RPop;
            redisCommand.Params = new object[] { source, destination };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 向列表尾部插入所有指定的值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="elements"></param>
        /// <returns>执行push操作后的列表长度</returns>
        public long RPush(string key, params object[] elements)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.RPush;
            redisCommand.Params = new object[] { key, elements };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 将元素插入到列表的尾部，当且仅当key存在且是一个列表时进行操作
        /// </summary>
        /// <param name="key"></param>
        /// <param name="elements"></param>
        /// <returns>执行push操作后的列表长度</returns>
        public long RPushX(string key, params object[] elements)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.RPushX;
            redisCommand.Params = new object[] { key, elements };
            return SendCommand<long>(redisCommand);
        }

        #endregion

        #region Pub/Sub
        /// <summary>
        /// 订阅一个或者多个符合给定模式的频道
        /// </summary>
        /// <param name="patterns"></param>
        /// <returns></returns>
        public string PSubscribe(params string[] patterns)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.PSubscribe;
            redisCommand.Params = patterns;
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 将消息message发送到指定的频道channel
        /// </summary>
        /// <param name="channel">频道</param>
        /// <param name="message">消息</param>
        /// <returns>接收到信息message的订阅者数量</returns>
        public long Publish(string channel, object message)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Publish;
            redisCommand.Params = new object[] { channel, message };
            return SendCommand<long>(redisCommand);
        }

        //public string Pubsub()

        /// <summary>
        /// 退订所有给定模式
        /// </summary>
        /// <param name="patterns"></param>
        /// <returns></returns>
        public string PUnSubscribe(params string[] patterns)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.PUnSubscribe;
            redisCommand.Params = patterns;
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 订阅给定的一个或者多个频道的信息
        /// </summary>
        /// <param name="channels"></param>
        /// <returns></returns>
        public string Subscribe(params string[] channels)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Subscribe;
            redisCommand.Params = channels;
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 订阅成功消息接收事件
        /// </summary>
        public Action<SubscribeEventArgs> Subscribed;

        /// <summary>
        /// 退订给定的频道
        /// </summary>
        /// <param name="channels"></param>
        /// <returns></returns>
        public string UnSubscribe(params string[] channels)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.UnSubscribe;
            redisCommand.Params = channels;
            return SendCommand<string>(redisCommand);
        }

        #endregion

        #region Key Command
        /// <summary>
        /// 删除多个key
        /// </summary>
        /// <param name="keys"></param>
        /// <returns>被删除key的数量</returns>
        public long Del(params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Del;
            redisCommand.Params = keys;
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 序列化给定key
        /// </summary>
        /// <param name="key"></param>
        /// <returns>key存在，返回序列化之后的值，不存在则返回null</returns>
        public string Dump(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Dump;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 判断key是否存在
        /// </summary>
        /// <param name="key"></param>
        /// <returns>1 存在，0 不存在</returns>
        public long Exists(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Exists;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 设置key的过期时间(seconds)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="seconds">秒</param>
        /// <returns>1：设置成功，0：key不存在</returns>
        public long Expire(string key, long seconds)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Expire;
            redisCommand.Params = new object[] { key, seconds };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 设置key的过期时间(timestamp)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="dateTime">时间，自动转换为unix时间戳</param>
        /// <returns>1：设置成功，0：key不存在</returns>
        public long ExpireAt(string key, DateTime dateTime)
        {
            //long unixTimeStamp = (dateTime.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
            long unixTimeStamp = (long)(dateTime - new DateTime(1970, 1, 1, 8, 0, 0)).TotalSeconds;
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ExpireAt;
            redisCommand.Params = new object[] { key, unixTimeStamp };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 查找匹配指定模式pattern的key
        /// </summary>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public IList<string> Keys(string pattern)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Keys;
            redisCommand.Params = new object[] { pattern };
            return SendCommand<IList<string>>(redisCommand);
        }

        /// <summary>
        /// 迁移键到指定的数据库实例中
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <param name="key"></param>
        /// <param name="destinationDb"></param>
        /// <param name="timeout">超时时间(毫秒)</param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public bool Migrate(string host, int port, string key, int destinationDb, int timeout, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Migrate;
            redisCommand.Params = new List<object> { host, port, key, destinationDb, timeout };
            if (string.IsNullOrEmpty(key) && keys.Length > 0)
            {
                redisCommand.Params.Add("keys");
                foreach (var item in keys)
                {
                    redisCommand.Params.Add(item);
                }
            }
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 将当前数据的key移动到指定的数据库db当中
        /// </summary>
        /// <param name="key"></param>
        /// <param name="db">数据库</param>
        /// <returns>1：key被移动，0：key没有被移动</returns>
        public long Move(string key, int db)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Move;
            redisCommand.Params = new object[] { key, db };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 从内部查看给定key的redis对象
        /// </summary>
        /// <param name="key"></param>
        /// <param name="subCommandOptions"></param>
        /// <returns></returns>
        public object Object(string key, SubCommandEnum subCommandOptions)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Object;
            redisCommand.Params = new object[] { subCommandOptions, key };
            return SendCommand<object>(redisCommand);
        }

        /// <summary>
        /// 删除给定Key的过期时间，使得key永不过期
        /// </summary>
        /// <param name="key"></param>
        /// <returns>1：移除过期时间成功，0：key不存在或者没有设置过期时间</returns>
        public long Persist(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Persist;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 设置key的过期时间(milliseconds)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="milliseconds">毫秒</param>
        /// <returns>1：设置成功，0：key不存在或设置失败</returns>
        public long PExpire(string key, long milliseconds)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.PExpire;
            redisCommand.Params = new object[] { key, milliseconds };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 设置key的过期时间(timestamp)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="dateTime">时间，自动转换为unix时间戳</param>
        /// <returns>1：设置成功，0：key不存在</returns>
        public long PExpireAt(string key, DateTime dateTime)
        {
            long unixTimeStamp = (dateTime.ToUniversalTime().Ticks - 621355968000000000) / 10000;
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.PExpireAt;
            redisCommand.Params = new object[] { key, unixTimeStamp };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 以毫秒为单位返回key的剩余过期时间
        /// </summary>
        /// <param name="key"></param>
        /// <returns>-2：key不存在，-1：key存在但没有设置剩余生存时间，其他：以毫秒为单位，返回key的剩余生存时间</returns>
        public long PTtl(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.PTtl;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 从当前数据库中随机返回一个Key
        /// </summary>
        /// <returns>当数据库不为空时，返回一个key，否则返回NULL</returns>
        public string RandomKey()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.RandomKey;
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 修改key的名字为newkey
        /// </summary>
        /// <param name="key">旧的key</param>
        /// <param name="newkey">新的Key</param>
        /// <returns></returns>
        public bool Rename(string key, string newkey)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Rename;
            redisCommand.Params = new object[] { key, newkey };
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 在新的key不存在时修改key的名称，若key不存在返回错误
        /// 在集群模式下，key和newkey必须在同一个哈希槽才能重命名
        /// </summary>
        /// <param name="key">旧的key</param>
        /// <param name="newkey">新的Key</param>
        /// <returns>1：成功，0：newkey已经存在</returns>
        public long RenameNx(string key, string newkey)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.RenameNx;
            redisCommand.Params = new object[] { key, newkey };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 反序列化Dump生成的值，并关联指定的key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="ttl">过期时间(毫秒)</param>
        /// <param name="serializedValue"></param>
        /// <returns></returns>
        public bool Restore(string key, long ttl, string serializedValue)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Restore;
            redisCommand.Params = new object[] { key, ttl, serializedValue };
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 迭代当前数据库中的键
        /// </summary>
        /// <param name="cursor"></param>
        /// <returns></returns>
        public object Scan(int cursor)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Scan;
            redisCommand.Params = new object[] { cursor };
            return SendCommand<object>(redisCommand);
        }

        /// <summary>
        /// 排序
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IList<string> Sort(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Sort;
            redisCommand.Params = new object[] { key };
            return SendCommand<IList<string>>(redisCommand);
        }

        /// <summary>
        /// 修改指定key的最后访问时间，忽略不存在的key
        /// </summary>
        /// <param name="keys"></param>
        /// <returns>被更新的Key个数</returns>
        public long Touch(params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Touch;
            redisCommand.Params = keys;
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 以秒为单位返回key的剩余过期时间
        /// </summary>
        /// <param name="key"></param>
        /// <returns>剩余超时秒数，-2：key不存在，-1：key存在但没有管理超时时间</returns>
        public long Ttl(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Ttl;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 以字符串的形式返回存储在key中的值的类型
        /// </summary>
        /// <param name="key"></param>
        /// <returns>返回key的类型，key不存在时返回null</returns>
        public string Type(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Type;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 删除指定的key，与Del不同的是，该命令执行实际的内存回收，因此是非阻塞的，而Del是阻塞的，将键与键空间断开连接，实际的删除将稍后异步进行
        /// </summary>
        /// <param name="keys"></param>
        /// <returns>断开连接Key的个数</returns>
        public long Unlink(params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Unlink;
            redisCommand.Params = keys;
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 阻塞当前客户端，直到所有先前的写入命令成功传输并且至少由指定数量的从节点复制完成
        /// </summary>
        /// <param name="numReplicas">从节点数量</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>接收写操作的从节点数量</returns>
        public long Wait(int numReplicas, long timeout)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Wait;
            redisCommand.Params = new object[] { numReplicas, timeout };
            return SendCommand<long>(redisCommand);
        }

        #endregion

        #region Server Command
        public bool FlushDB()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.FlushDB;
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }
        #endregion

        #region Connection Command
        //public string Auth

        /// <summary>
        /// 打印一个给定的信息message
        /// </summary>
        /// <returns>返回字符串message本身</returns>
        public string Echo(string message)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Echo;
            redisCommand.Params = new object[] { message };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// Ping 通常用来测试连接是否存活，或者测试延迟
        /// </summary>
        /// <returns>连接正常返回PONG，否则返回一个连接错误</returns>
        public string Ping()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Ping;
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 请求服务器关闭连接，在当前连接上的待处理请求完成后，立刻断开连接
        /// </summary>
        /// <returns>总是返回OK</returns>
        public string Quit()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Quit;
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 切换到指定的数据库，dbIndex从0开始的整数，默认使用数据库0，集群时不能使用select，集群只支持数据库0
        /// <param name="dbIndex">数据库编号0-15</param>
        /// </summary>
        /// <returns>总是返回OK</returns>
        public bool Select(int dbIndex)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Select;
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        #endregion

        /// <summary>
        /// Pipelining(批量发送)
        /// </summary>
        /// <param name="redisCommands"></param>
        /// <returns></returns>
        public IList<object> Pipelining(IList<RedisCommand> redisCommands)
        {
            return new List<object>(redisCommands.Count);
        }

    }
}
