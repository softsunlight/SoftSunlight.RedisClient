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
                var obj = Activator.CreateInstance(typeof(T));
                obj = Convert.ToInt64(orignalData.Substring(1, orignalData.Length - 3));
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
                            autoResetEvent.Set();
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

        public string Get(string key)
        {
            RedisCommand redisCommand = new RedisCommand();
            redisCommand.RedisCommands = RedisCommandEnum.Get;
            redisCommand.Params = new object[] { key };
            return SendCommand<string>(redisCommand);
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
        public long Publish(string channel, string message)
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
            long unixTimeStamp = (dateTime.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
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

        //public bool Migrate()

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

        //public long Object()

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

        //public bool Restore()

        //public object Scan()

        //public string Sort()

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
            return new List<object>();
        }

    }
}
