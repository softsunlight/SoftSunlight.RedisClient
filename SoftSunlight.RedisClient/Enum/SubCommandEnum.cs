using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient.Enum
{
    public enum SubCommandEnum
    {
        /// <summary>
        /// key引用所存储的值的次数
        /// </summary>
        RefCount,
        /// <summary>
        /// key所使用的内部表示
        /// </summary>
        Encoding,
        /// <summary>
        /// key自存储以来的空闲时间，以秒为单位
        /// </summary>
        IdleTime,
    }
}
