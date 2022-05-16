using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient
{
    /// <summary>
    /// Redis命令
    /// </summary>
    public class RedisCommand
    {
        /// <summary>
        /// 命令名称
        /// </summary>
        public RedisCommandEnum RedisCommands { get; set; }
        /// <summary>
        /// 参数
        /// </summary>
        public IList<object> Params { get; set; }
    }
}
