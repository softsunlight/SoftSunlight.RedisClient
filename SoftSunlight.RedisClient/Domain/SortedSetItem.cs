using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient.Domain
{
    public class SortedSetItem
    {
        /// <summary>
        /// 整数或双精度浮点数，可正可负
        /// </summary>
        public double Score { get; set; }
        /// <summary>
        /// 数据成员
        /// </summary>
        public object Member { get; set; }
    }
}
