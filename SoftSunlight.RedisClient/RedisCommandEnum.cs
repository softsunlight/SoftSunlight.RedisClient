using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient
{
    /// <summary>
    /// Redis命令列表
    /// </summary>
    public enum RedisCommandEnum
    {
        #region Key
        Del,
        Dump,
        Exists,
        Expire,
        ExpireAt,
        Keys,
        Migrate,
        Move,
        Object,
        Persist,
        PExpire,
        PExpireAt,
        PTtl,
        RandomKey,
        Rename,
        RenameNx,
        Restore,
        Scan,
        Sort,
        Touch,
        Ttl,
        Type,
        Unlink,
        Wait,
        #endregion

        #region String
        Append,
        BitCount,
        Get,
        Set,
        #endregion

        #region Pub/Sub
        PSubscribe,
        Publish,
        Pubsub,
        PUnSubscribe,
        Subscribe,
        UnSubscribe,
        #endregion

        #region Connection
        Auth,
        Echo,
        Ping,
        Quit,
        Select,
        #endregion

        #region Server
        FlushDB,
        #endregion
    }
}
