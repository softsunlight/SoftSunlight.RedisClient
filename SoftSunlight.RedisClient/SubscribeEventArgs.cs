using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient
{
    public class SubscribeEventArgs : EventArgs
    {
        /// <summary>
        /// 频道
        /// </summary>
        public string Channel { get; set; }
        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; }
    }
}
