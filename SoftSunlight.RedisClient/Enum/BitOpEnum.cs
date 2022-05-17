using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient.Enum
{
    /// <summary>
    /// BitOp Operation
    /// </summary>
    public enum BitOpEnum
    {
        /// <summary>
        /// 逻辑并
        /// </summary>
        And,
        /// <summary>
        /// 逻辑或
        /// </summary>
        Or,
        /// <summary>
        /// 逻辑异或
        /// </summary>
        XOr,
        /// <summary>
        /// 逻辑非
        /// </summary>
        Not
    }
}
