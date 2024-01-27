// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Config;

namespace Nethermind.Db.Rocks.Config;

[ConfigCategory(HiddenFromDocs = true)]
public interface IDbConfig : IConfig
{
    ulong SharedBlockCacheSize { get; set; }
    public bool SkipMemoryHintSetting { get; set; }

    ulong WriteBufferSize { get; set; }
    uint WriteBufferNumber { get; set; }
    ulong BlockCacheSize { get; set; }
    bool CacheIndexAndFilterBlocks { get; set; }
    int? MaxOpenFiles { get; set; }
    uint RecycleLogFileNum { get; set; }
    bool WriteAheadLogSync { get; set; }
    long? MaxBytesPerSec { get; set; }
    int? BlockSize { get; set; }
    ulong? ReadAheadSize { get; set; }
    bool? UseDirectReads { get; set; }
    bool? UseDirectIoForFlushAndCompactions { get; set; }
    bool? DisableCompression { get; set; }
    ulong? CompactionReadAhead { get; set; }
    IDictionary<string, string>? AdditionalRocksDbOptions { get; set; }
    ulong? MaxBytesForLevelBase { get; set; }
    ulong TargetFileSizeBase { get; set; }
    int TargetFileSizeMultiplier { get; set; }

    ulong ReceiptsDbWriteBufferSize { get; set; }
    uint ReceiptsDbWriteBufferNumber { get; set; }
    ulong ReceiptsDbBlockCacheSize { get; set; }
    bool ReceiptsDbCacheIndexAndFilterBlocks { get; set; }
    int? ReceiptsDbMaxOpenFiles { get; set; }
    long? ReceiptsDbMaxBytesPerSec { get; set; }
    int? ReceiptsDbBlockSize { get; set; }
    bool? ReceiptsDbUseDirectReads { get; set; }
    bool? ReceiptsDbUseDirectIoForFlushAndCompactions { get; set; }
    ulong? ReceiptsDbCompactionReadAhead { get; set; }
    ulong ReceiptsDbTargetFileSizeBase { get; set; }
    IDictionary<string, string>? ReceiptsDbAdditionalRocksDbOptions { get; set; }

    ulong BlocksDbWriteBufferSize { get; set; }
    uint BlocksDbWriteBufferNumber { get; set; }
    ulong BlocksDbBlockCacheSize { get; set; }
    bool BlocksDbCacheIndexAndFilterBlocks { get; set; }
    int? BlocksDbMaxOpenFiles { get; set; }
    long? BlocksDbMaxBytesPerSec { get; set; }
    int? BlocksBlockSize { get; set; }
    bool? BlocksDbUseDirectReads { get; set; }
    bool? BlocksDbUseDirectIoForFlushAndCompactions { get; set; }
    ulong? BlocksDbCompactionReadAhead { get; set; }
    IDictionary<string, string>? BlocksDbAdditionalRocksDbOptions { get; set; }

    ulong HeadersDbWriteBufferSize { get; set; }
    uint HeadersDbWriteBufferNumber { get; set; }
    ulong HeadersDbBlockCacheSize { get; set; }
    bool HeadersDbCacheIndexAndFilterBlocks { get; set; }
    int? HeadersDbMaxOpenFiles { get; set; }
    long? HeadersDbMaxBytesPerSec { get; set; }
    int? HeadersDbBlockSize { get; set; }
    bool? HeadersDbUseDirectReads { get; set; }
    bool? HeadersDbUseDirectIoForFlushAndCompactions { get; set; }
    ulong? HeadersDbCompactionReadAhead { get; set; }
    IDictionary<string, string>? HeadersDbAdditionalRocksDbOptions { get; set; }
    ulong? HeadersDbMaxBytesForLevelBase { get; set; }

    ulong BlockNumbersDbWriteBufferSize { get; set; }
    uint BlockNumbersDbWriteBufferNumber { get; set; }
    ulong BlockNumbersDbBlockCacheSize { get; set; }
    bool BlockNumbersDbCacheIndexAndFilterBlocks { get; set; }
    int? BlockNumbersDbMaxOpenFiles { get; set; }
    long? BlockNumbersDbMaxBytesPerSec { get; set; }
    int? BlockNumbersDbBlockSize { get; set; }
    bool? BlockNumbersDbUseDirectReads { get; set; }
    bool? BlockNumbersDbUseDirectIoForFlushAndCompactions { get; set; }
    ulong? BlockNumbersDbCompactionReadAhead { get; set; }
    IDictionary<string, string>? BlockNumbersDbAdditionalRocksDbOptions { get; set; }
    ulong? BlockNumbersDbMaxBytesForLevelBase { get; set; }

    ulong BlockInfosDbWriteBufferSize { get; set; }
    uint BlockInfosDbWriteBufferNumber { get; set; }
    ulong BlockInfosDbBlockCacheSize { get; set; }
    bool BlockInfosDbCacheIndexAndFilterBlocks { get; set; }
    int? BlockInfosDbMaxOpenFiles { get; set; }
    long? BlockInfosDbMaxBytesPerSec { get; set; }
    int? BlockInfosDbBlockSize { get; set; }
    bool? BlockInfosDbUseDirectReads { get; set; }
    bool? BlockInfosDbUseDirectIoForFlushAndCompactions { get; set; }
    ulong? BlockInfosDbCompactionReadAhead { get; set; }
    IDictionary<string, string>? BlockInfosDbAdditionalRocksDbOptions { get; set; }

    ulong PendingTxsDbWriteBufferSize { get; set; }
    uint PendingTxsDbWriteBufferNumber { get; set; }
    ulong PendingTxsDbBlockCacheSize { get; set; }
    bool PendingTxsDbCacheIndexAndFilterBlocks { get; set; }
    int? PendingTxsDbMaxOpenFiles { get; set; }
    long? PendingTxsDbMaxBytesPerSec { get; set; }
    int? PendingTxsDbBlockSize { get; set; }
    bool? PendingTxsDbUseDirectReads { get; set; }
    bool? PendingTxsDbUseDirectIoForFlushAndCompactions { get; set; }
    ulong? PendingTxsDbCompactionReadAhead { get; set; }
    IDictionary<string, string>? PendingTxsDbAdditionalRocksDbOptions { get; set; }

    ulong CodeDbWriteBufferSize { get; set; }
    uint CodeDbWriteBufferNumber { get; set; }
    ulong CodeDbBlockCacheSize { get; set; }
    bool CodeDbCacheIndexAndFilterBlocks { get; set; }
    int? CodeDbMaxOpenFiles { get; set; }
    long? CodeDbMaxBytesPerSec { get; set; }
    int? CodeDbBlockSize { get; set; }
    bool? CodeUseDirectReads { get; set; }
    bool? CodeUseDirectIoForFlushAndCompactions { get; set; }
    ulong? CodeCompactionReadAhead { get; set; }
    IDictionary<string, string>? CodeDbAdditionalRocksDbOptions { get; set; }

    ulong BloomDbWriteBufferSize { get; set; }
    uint BloomDbWriteBufferNumber { get; set; }
    ulong BloomDbBlockCacheSize { get; set; }
    bool BloomDbCacheIndexAndFilterBlocks { get; set; }
    int? BloomDbMaxOpenFiles { get; set; }
    long? BloomDbMaxBytesPerSec { get; set; }
    IDictionary<string, string>? BloomDbAdditionalRocksDbOptions { get; set; }

    ulong WitnessDbWriteBufferSize { get; set; }
    uint WitnessDbWriteBufferNumber { get; set; }
    ulong WitnessDbBlockCacheSize { get; set; }
    bool WitnessDbCacheIndexAndFilterBlocks { get; set; }
    int? WitnessDbMaxOpenFiles { get; set; }
    long? WitnessDbMaxBytesPerSec { get; set; }
    int? WitnessDbBlockSize { get; set; }
    bool? WitnessUseDirectReads { get; set; }
    bool? WitnessUseDirectIoForFlushAndCompactions { get; set; }
    ulong? WitnessCompactionReadAhead { get; set; }
    IDictionary<string, string>? WitnessDbAdditionalRocksDbOptions { get; set; }

    ulong CanonicalHashTrieDbWriteBufferSize { get; set; }
    uint CanonicalHashTrieDbWriteBufferNumber { get; set; }
    ulong CanonicalHashTrieDbBlockCacheSize { get; set; }
    bool CanonicalHashTrieDbCacheIndexAndFilterBlocks { get; set; }
    int? CanonicalHashTrieDbMaxOpenFiles { get; set; }
    long? CanonicalHashTrieDbMaxBytesPerSec { get; set; }
    int? CanonicalHashTrieDbBlockSize { get; set; }
    bool? CanonicalHashTrieUseDirectReads { get; set; }
    bool? CanonicalHashTrieUseDirectIoForFlushAndCompactions { get; set; }
    ulong? CanonicalHashTrieCompactionReadAhead { get; set; }
    IDictionary<string, string>? CanonicalHashTrieDbAdditionalRocksDbOptions { get; set; }

    ulong MetadataDbWriteBufferSize { get; set; }
    uint MetadataDbWriteBufferNumber { get; set; }
    ulong MetadataDbBlockCacheSize { get; set; }
    bool MetadataDbCacheIndexAndFilterBlocks { get; set; }
    int? MetadataDbMaxOpenFiles { get; set; }
    long? MetadataDbMaxBytesPerSec { get; set; }
    int? MetadataDbBlockSize { get; set; }
    bool? MetadataUseDirectReads { get; set; }
    bool? MetadataUseDirectIoForFlushAndCompactions { get; set; }
    ulong? MetadataCompactionReadAhead { get; set; }
    IDictionary<string, string>? MetadataDbAdditionalRocksDbOptions { get; set; }

    ulong StateDbWriteBufferSize { get; set; }
    uint StateDbWriteBufferNumber { get; set; }
    ulong StateDbBlockCacheSize { get; set; }
    bool StateDbCacheIndexAndFilterBlocks { get; set; }
    int? StateDbMaxOpenFiles { get; set; }
    long? StateDbMaxBytesPerSec { get; set; }
    int? StateDbBlockSize { get; set; }
    bool? StateDbUseDirectReads { get; set; }
    bool? StateDbUseDirectIoForFlushAndCompactions { get; set; }
    ulong? StateDbCompactionReadAhead { get; set; }
    bool? StateDbDisableCompression { get; set; }
    int StateDbTargetFileSizeMultiplier { get; set; }
    IDictionary<string, string>? StateDbAdditionalRocksDbOptions { get; set; }

    /// <summary>
    /// Enables DB Statistics - https://github.com/facebook/rocksdb/wiki/Statistics
    /// It can has a RocksDB performance hit between 5 and 10%.
    /// </summary>
    bool EnableDbStatistics { get; set; }
    bool EnableMetricsUpdater { get; set; }
    /// <summary>
    /// If not zero, dump rocksdb.stats to LOG every stats_dump_period_sec
    /// Default: 600 (10 min)
    /// </summary>
    uint StatsDumpPeriodSec { get; set; }

    int HighPriorityThreadCount { get; set; }
    int LowPriorityThreadCount { get; set; }
}
