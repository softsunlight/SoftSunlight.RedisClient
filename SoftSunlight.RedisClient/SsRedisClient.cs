using SoftSunlight.RedisClient.Domain;
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

        #region Set Command
        /// <summary>
        /// 将一个或多个成员加入到集合，已经存在的就忽略
        /// </summary>
        /// <param name="key"></param>
        /// <param name="members"></param>
        /// <returns>返回新成功添加到集合里元素的数量</returns>
        public long SAdd(string key, params object[] members)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SAdd;
            redisCommand.Params = new object[] { key, members };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回集合中元素的数量
        /// </summary>
        /// <param name="key"></param>
        /// <returns>集合元素的数量</returns>
        public long SCard(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SCard;
            redisCommand.Params = new object[] { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回第一个集合与其他集合之间的差异，也可以说是第一个集合中独有的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="keys"></param>
        /// <returns>集合元素的数量</returns>
        public IList<object> SDiff(string key, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SDiff;
            redisCommand.Params = new object[] { key, keys };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 和SDiff类似，不同的是它将结果保存到destination集合
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="key"></param>
        /// <param name="keys"></param>
        /// <returns>集合元素的数量</returns>
        public long SDiffStore(string destination, string key, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SDiffStore;
            redisCommand.Params = new object[] { destination, key, keys };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回所有给定集合的成员交集
        /// </summary>
        /// <param name="keys"></param>
        /// <returns>集合元素的数量</returns>
        public IList<object> SInter(params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SInter;
            redisCommand.Params = new object[] { keys };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 与SInter命令类似，不同的是他将结果保存到destination集合中
        /// </summary>
        /// <param name="keys"></param>
        /// <returns>集合元素的数量</returns>
        public long SInterStore(string destination, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SInterStore;
            redisCommand.Params = new object[] { destination, keys };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 判断元素是否是集合的成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="member"></param>
        /// <returns>1：是集合的成员，0：不是或者key不存在</returns>
        public long SIsMember(string key, object member)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SIsMember;
            redisCommand.Params = new object[] { key, member };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回存储在key中的集合的所有成员
        /// </summary>
        /// <param name="key"></param>
        /// <returns>集合中的所有成员</returns>
        public IList<object> SMembers(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SMembers;
            redisCommand.Params = new object[] { key };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>检查给定的member是不是特定集合的成员
        /// </summary>
        /// <param name="key"></param>
        /// <returns>按命令助攻member出现的顺序，返回是否是集合成员的判断</returns>
        public IList<object> SMIsMember(string key, params object[] members)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SMIsMember;
            redisCommand.Params = new object[] { key, members };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 从集合source中移动成员member到集合destination
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="member"></param>
        /// <returns>1：成功，0：要移动的元素不是source的成员，什么也不执行</returns>
        public long SMove(string source, string destination, object member)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SMove;
            redisCommand.Params = new object[] { source, destination, member };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 从集合中删除并返回一个随机元素
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string SPop(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SPop;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 从集合中删除并返回count个随机元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public string SPop(string key, int count)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SPop;
            redisCommand.Params = new object[] { key, count };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 随机返回集合key中的一个随机元素
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string SRandMember(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SRandMember;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 从集合中返回count个随机元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public string SRandMember(string key, int count)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SRandMember;
            redisCommand.Params = new object[] { key, count };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 删除集合中指定的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="members"></param>
        /// <returns>被删除元素的个数，不含不存在的元素</returns>
        public long SRem(string key, params object[] members)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SRem;
            redisCommand.Params = new object[] { key, members };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 遍历集合中的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="cursor"></param>
        /// <returns></returns>
        public IList<object> SScan(string key, int cursor)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SScan;
            redisCommand.Params = new object[] { key, cursor };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 遍历集合中的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="cursor"></param>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public IList<object> SScan(string key, int cursor, string pattern)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SScan;
            redisCommand.Params = new object[] { key, cursor, "MATCH", pattern };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 遍历集合中的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="cursor"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public IList<object> SScan(string key, int cursor, int count)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SScan;
            redisCommand.Params = new object[] { key, cursor, "COUNT", count };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 遍历集合中的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="cursor"></param>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public IList<object> SScan(string key, int cursor, string pattern, int count)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SScan;
            redisCommand.Params = new object[] { key, cursor, "MATCH", pattern, "COUNT", count };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 返回所有给定集合的并集
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IList<object> SUnion(params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SUnion;
            redisCommand.Params = new object[] { keys };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 将所有给定集合的并集存储到destination中
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public long SUnionStore(string destination, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.SUnionStore;
            redisCommand.Params = new object[] { destination, keys };
            return SendCommand<long>(redisCommand);
        }
        #endregion

        #region SortedSet Command
        /// <summary>
        /// 添加有序集合
        /// </summary>
        /// <param name="key"></param>
        /// <param name="sortedSetItems"></param>
        /// <returns></returns>
        public long ZAdd(string key, IList<SortedSetItem> sortedSetItems)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZAdd;
            redisCommand.Params = new List<object> { key };
            if (sortedSetItems != null)
            {
                foreach (var item in sortedSetItems)
                {
                    redisCommand.Params.Add(item.Score);
                    redisCommand.Params.Add(item.Member);
                }
            }
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回有序集的成员个数
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public long ZCard(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZCard;
            redisCommand.Params = new List<object> { key };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回有序集合中score在min和max之间的成员的数量
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public long ZCount(string key, double min, double max)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZCount;
            redisCommand.Params = new List<object> { key, min, max };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 为成员member的score值加上增量increment，increment为负数则减去
        /// </summary>
        /// <param name="key"></param>
        /// <param name="increment"></param>
        /// <param name="member"></param>
        /// <returns>返回新的score值</returns>
        public string ZIncrBy(string key, double increment, object member)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZIncrBy;
            redisCommand.Params = new List<object> { key, increment, member };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 返回集合的交集
        /// </summary>
        /// <param name="numKeys"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IList<object> ZInter(int numKeys, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZInter;
            redisCommand.Params = new List<object> { numKeys, keys };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 计算numKeys个有序集合的交集，并且把结果放到destination中
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="numKeys"></param>
        /// <param name="keys"></param>
        /// <returns>结果集destination中元素个数</returns>
        public long ZInterStore(string destination, int numKeys, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZInterStore;
            redisCommand.Params = new List<object> { numKeys, keys };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回有序集中值在min和max之间的成员个数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public long ZLexCount(string key, double min, double max)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZLexCount;
            redisCommand.Params = new List<object> { key, min, max };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回有序集中指定成员的members的scores
        /// </summary>
        /// <param name="key"></param>
        /// <param name="members"></param>
        /// <returns></returns>
        public IList<object> ZMScore(string key, params object[] members)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZMScore;
            redisCommand.Params = new List<object> { key, members };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 删除并返回最多count个有序集合中的最高得分的成员
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IList<object> ZPopMax(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZPopMax;
            redisCommand.Params = new List<object> { key };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 删除并返回最多count个有序集合中的最高得分的成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public IList<object> ZPopMax(string key, int count)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZPopMax;
            redisCommand.Params = new List<object> { key, count };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 删除并返回最多count个有序集合中的最低得分的成员
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IList<object> ZPopMin(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZPopMin;
            redisCommand.Params = new List<object> { key };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 删除并返回最多count个有序集合中的最低得分的成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public IList<object> ZPopMin(string key, int count)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZPopMin;
            redisCommand.Params = new List<object> { key, count };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 返回指定区间的成员，成员按分数值递增排序
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="withScores"></param>
        /// <returns></returns>
        public IList<object> ZRange(string key, int start, int end, bool withScores = false)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRange;
            redisCommand.Params = new List<object> { key, start, end };
            if (withScores)
            {
                redisCommand.Params.Add("WITHSCORES");
            }
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public IList<object> ZRangeByLex(string key, double min, double max)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRangeByLex;
            redisCommand.Params = new List<object> { key, min, max };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 返回有序集中所有score值介于min和max(包括等于min或max)之间的成员，有序集成员按score值递增
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public IList<object> ZRangeByScore(string key, double min, double max, bool withScores = false)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRangeByScore;
            redisCommand.Params = new List<object> { key, min, max };
            if (withScores)
            {
                redisCommand.Params.Add("WITHSCORES");
            }
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 返回有序集中成员的排名,成员按score值从低到高
        /// </summary>
        /// <param name="key"></param>
        /// <param name="member"></param>
        /// <returns></returns>
        public object ZRank(string key, object member)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRank;
            redisCommand.Params = new List<object> { key, member };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 从有序集合key中删除指定的成员member
        /// </summary>
        /// <param name="key"></param>
        /// <param name="members"></param>
        /// <returns></returns>
        public long ZRem(string key, params object[] members)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRem;
            redisCommand.Params = new List<object> { key, members };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 删除成员名称按字典由低到高排序介于min和max之间的所有成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public long ZRemRangeByLex(string key, double min, double max)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRemRangeByLex;
            redisCommand.Params = new List<object> { key, min, max };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 移除有序集中指定排名区间的所有成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public long ZRemRangeByRank(string key, int start, int end)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRemRangeByRank;
            redisCommand.Params = new List<object> { key, start, end };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 移除有序集中所有score值介于min和max之间的成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public long ZRemRangeByRank(string key, double min, double max)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRemRangeByRank;
            redisCommand.Params = new List<object> { key, min, max };
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 返回有序集中指定区间内的成员，成员按score值递减
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="withScores"></param>
        /// <returns></returns>
        public IList<object> ZRevRange(string key, int start, int end, bool withScores = false)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRevRange;
            redisCommand.Params = new List<object> { key, start, end };
            if (withScores)
            {
                redisCommand.Params.Add("WITHSCORES");
            }
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 当以相同的分数插入排序集中的所有元素时，为了强制按字典顺序排序，返回排序集中key值介于max和min之间的成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="max"></param>
        /// <param name="min"></param>
        /// <returns></returns>
        public IList<object> ZRevRangeByLex(string key, string max, string min)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRevRangeByLex;
            redisCommand.Params = new List<object> { key, max, min };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 当以相同的分数插入排序集中的所有元素时，为了强制按字典顺序排序，返回排序集中score值介于max和min之间的成员
        /// </summary>
        /// <param name="key"></param>
        /// <param name="max"></param>
        /// <param name="min"></param>
        /// <returns></returns>
        public IList<object> ZRevRangeByScore(string key, double max, double min, bool withScores = false)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRevRangeByScore;
            redisCommand.Params = new List<object> { key, max, min };
            if (withScores)
            {
                redisCommand.Params.Add("WITHSCORES");
            }
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 返回有序集中成员member的排名，有序集成员按score值从高到低排列
        /// </summary>
        /// <param name="key"></param>
        /// <param name="member"></param>
        /// <returns></returns>
        public object ZRevRank(string key, object member)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZRevRank;
            redisCommand.Params = new List<object> { key, member };
            return SendCommand<object>(redisCommand);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="cursor"></param>
        /// <returns></returns>
        public IList<object> ZScan(string key, int cursor)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZScan;
            redisCommand.Params = new List<object> { key, cursor };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 返回有序集中成员的分数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="member"></param>
        /// <returns></returns>
        public string ZScore(string key, object member)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZScore;
            redisCommand.Params = new List<object> { key, member };
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 返回集合的并集
        /// </summary>
        /// <param name="numKeys"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IList<object> ZUnion(int numKeys, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZUnion;
            redisCommand.Params = new List<object> { numKeys, keys };
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 计算给定numKeys个有序集合的并集，并把结果保存到destination中
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="nunKeys"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public long ZUnionStore(string destination, int numKeys, params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.ZUnionStore;
            redisCommand.Params = new List<object> { destination, numKeys, keys };
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

        #region Transaction Command
        /// <summary>
        /// 取消事务
        /// </summary>
        /// <returns></returns>
        public bool Discard()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Discard;
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 执行事务队列内的所有命令
        /// </summary>
        /// <returns></returns>
        public IList<object> Exec()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Exec;
            return SendCommand<IList<object>>(redisCommand);
        }

        /// <summary>
        /// 开启事务
        /// </summary>
        /// <returns></returns>
        public bool Multi()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Multi;
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 取消对所有Key的监视
        /// </summary>
        /// <returns></returns>
        public bool UnWatch()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.UnWatch;
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 标记要监视的Keys,以便有条件的执行事务，一旦有键被修改或删除，之后的事务就不会执行
        /// </summary>
        /// <returns></returns>
        public bool Watch(params string[] keys)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Watch;
            redisCommand.Params = new object[] { keys };
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

        #region Server Command
        /// <summary>
        /// 异步执行一个AOF文件重写操作
        /// </summary>
        /// <returns></returns>
        public string BgRewriteAof()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BgRewriteAof;
            return SendCommand<string>(redisCommand);
        }

        /// <summary>
        /// 后台保存DB
        /// </summary>
        /// <returns></returns>
        public bool BgSave()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.BgSave;
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 返回当前数据库中key的数量
        /// </summary>
        /// <returns></returns>
        public long DbSize()
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.DbSize;
            return SendCommand<long>(redisCommand);
        }

        /// <summary>
        /// 清空整个redis中的数据(清空所有数据库中所有key)
        /// </summary>
        /// <param name="async">是否异步操作</param>
        /// <returns></returns>
        public bool FlushAll(bool async = false)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.FlushAll;
            if (async)
            {
                redisCommand.Params = new object[] { async };
            }
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 清空当前select数据库中所有key
        /// </summary>
        /// <param name="async"></param>
        /// <returns></returns>
        public bool FlushDB(bool async = false)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.FlushDB;
            if (async)
            {
                redisCommand.Params = new object[] { async };
            }
            return SendCommand<string>(redisCommand).Equals("OK", StringComparison.OrdinalIgnoreCase) ? true : false;
        }

        /// <summary>
        /// 返回redis服务器的各种信息和统计数值  
        /// </summary>
        /// <returns></returns>
        //public string Info()
        //{

        //}
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
