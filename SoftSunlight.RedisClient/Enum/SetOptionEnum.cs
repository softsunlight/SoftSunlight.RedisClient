using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient.Enum
{
    /// <summary>
    /// set命令键设置选项枚举类
    /// </summary>
    public enum SetOptionEnum
    {
        /// <summary>
        /// key存在时设置
        /// </summary>
        NX,
        /// <summary>
        /// key不存在时设置
        /// </summary>
        XX
    }
}
