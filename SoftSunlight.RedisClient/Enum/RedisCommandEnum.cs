using SoftSunlight.RedisClient.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient.Enum
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
        BitOp,
        BitPos,
        Decr,
        DecrBy,
        Get,
        GetBit,
        GetRange,
        GetSet,
        Incr,
        IncrBy,
        IncrByFloat,
        MGet,
        MSet,
        MSetNx,
        PSetEx,
        Set,
        SetBit,
        SetEx,
        SetNx,
        SetRange,
        StrAlgo,
        StrLen,
        #endregion

        #region Hash
        HDel,
        HExists,
        HGet,
        HGetAll,
        HIncrBy,
        HIncrByFloat,
        HKeys,
        HLen,
        HMGet,
        HMSet,
        HScan,
        HSet,
        HSetNx,
        HStrLen,
        HVals,
        #endregion

        #region List
        BLMove,
        BLPop,
        BRPop,
        BRPopLPush,
        LIndex,
        LInsert,
        LLen,
        LMove,
        LPop,
        LPos,
        LPush,
        LPushX,
        LRange,
        LRem,
        LSet,
        LTrim,
        RPop,
        RPopLPush,
        RPush,
        RPushX,
        #endregion

        #region Set
        SAdd,
        SCard,
        SDiff,
        SDiffStore,
        SInter,
        SInterStore,
        SIsMember,
        SMembers,
        SMIsMember,
        SMove,
        SPop,
        SRandMember,
        SRem,
        SScan,
        SUnion,
        SUnionStore,
        #endregion

        #region SortedSet
        ZAdd,
        ZCard,
        ZCount,
        ZIncrBy,
        ZInter,
        ZInterStore,
        ZLexCount,
        ZMScore,
        ZPopMax,
        ZPopMin,
        ZRange,
        ZRangeByLex,
        ZRangeByScore,
        ZRank,
        ZRem,
        ZRemRangeByLex,
        ZRemRangeByRank,
        ZRemRangeByScore,
        ZRevRange,
        ZRevRangeByLex,
        ZRevRangeByScore,
        ZRevRank,
        ZScan,
        ZScore,
        ZUnion,
        ZUnionStore,
        #endregion

        #region Pub/Sub
        PSubscribe,
        Publish,
        Pubsub,
        PUnSubscribe,
        Subscribe,
        UnSubscribe,
        #endregion

        #region Transcation
        Discard,
        Exec,
        Multi,
        UnWatch,
        Watch,
        #endregion

        #region Connection
        Auth,
        Echo,
        Ping,
        Quit,
        Select,
        #endregion

        #region Server
        BgRewriteAof,
        BgSave,
        Command,
        DbSize,
        FlushAll,
        FlushDB,
        Info,
        LastSave,
        Lolwut,
        Monitor,
        PSync,
        ReplicAof,
        Role,
        Save,
        ShutDown,
        SlaveOf,
        SlowLog,
        SwapDb,
        Sync,
        Time,
        #endregion

        #region Bloom Filter
        [CommandAlias("BF.Add")]
        BFAdd,
        [CommandAlias("BF.Exists")]
        BFExists,
        #endregion
    }
}
