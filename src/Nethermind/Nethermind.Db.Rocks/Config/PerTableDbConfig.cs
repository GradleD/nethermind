// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nethermind.Core.Extensions;

namespace Nethermind.Db.Rocks.Config;

public class PerTableDbConfig
{
    private readonly string _tableName;
    private readonly IDbConfig _dbConfig;
    private readonly DbSettings _settings;

    public PerTableDbConfig(IDbConfig dbConfig, DbSettings dbSettings, string? columnName = null)
    {
        _dbConfig = dbConfig;
        _settings = dbSettings;
        _tableName = _settings.DbName;
        if (columnName is not null)
        {
            _tableName += columnName;
        }
    }

    public bool CacheIndexAndFilterBlocks => _settings.CacheIndexAndFilterBlocks ?? ReadConfig<bool>(nameof(CacheIndexAndFilterBlocks));

    public ulong BlockCacheSize => _settings.BlockCacheSize ?? ReadConfig<ulong>(nameof(BlockCacheSize));

    public ulong WriteBufferSize => _settings.WriteBufferSize ?? ReadConfig<ulong>(nameof(WriteBufferSize));

    public ulong WriteBufferNumber => _settings.WriteBufferNumber ?? ReadConfig<uint>(nameof(WriteBufferNumber));

    public IDictionary<string, string>? AdditionalRocksDbOptions => ReadConfig<IDictionary<string, string>?>(nameof(AdditionalRocksDbOptions));

    public int? MaxOpenFiles => ReadConfig<int?>(nameof(MaxOpenFiles));
    public long? MaxBytesPerSec => ReadConfig<long?>(nameof(MaxBytesPerSec));
    public uint RecycleLogFileNum => ReadConfig<uint>(nameof(RecycleLogFileNum));
    public bool WriteAheadLogSync => ReadConfig<bool>(nameof(WriteAheadLogSync));
    public bool? UseDirectReads => ReadConfig<bool?>(nameof(UseDirectReads));
    public bool? UseDirectIoForFlushAndCompactions => ReadConfig<bool?>(nameof(UseDirectIoForFlushAndCompactions));
    public int? BlockSize => ReadConfig<int?>(nameof(BlockSize));
    public ulong? ReadAheadSize => ReadConfig<ulong?>(nameof(ReadAheadSize));
    public bool EnableDbStatistics => _dbConfig.EnableDbStatistics;
    public uint StatsDumpPeriodSec => _dbConfig.StatsDumpPeriodSec;
    public bool? DisableCompression => ReadConfig<bool?>(nameof(DisableCompression));
    public bool? UseLz4 => ReadConfig<bool?>(nameof(UseLz4));
    public ulong? CompactionReadAhead => ReadConfig<ulong?>(nameof(CompactionReadAhead));
    public ulong MaxBytesForLevelBase => ReadConfig<ulong>(nameof(MaxBytesForLevelBase));
    public ulong TargetFileSizeBase => ReadConfig<ulong>(nameof(TargetFileSizeBase));
    public int TargetFileSizeMultiplier => ReadConfig<int>(nameof(TargetFileSizeMultiplier));
    public bool UseTwoLevelIndex => ReadConfig<bool>(nameof(UseTwoLevelIndex));
    public bool UseHashIndex => ReadConfig<bool>(nameof(UseHashIndex));
    public ulong? PrefixExtractorLength => ReadConfig<ulong?>(nameof(PrefixExtractorLength));
    public bool AllowMmapReads => ReadConfig<bool>(nameof(AllowMmapReads));
    public bool? VerifyChecksum => ReadConfig<bool?>(nameof(VerifyChecksum));
    public double MaxBytesForLevelMultiplier => ReadConfig<double>(nameof(MaxBytesForLevelMultiplier));
    public ulong? MaxCompactionBytes => ReadConfig<ulong?>(nameof(MaxCompactionBytes));
    public int MinWriteBufferNumberToMerge => ReadConfig<int>(nameof(MinWriteBufferNumberToMerge));
    public ulong? RowCacheSize => ReadConfig<ulong?>(nameof(RowCacheSize));
    public bool OptimizeFiltersForHits => ReadConfig<bool>(nameof(OptimizeFiltersForHits));
    public bool OnlyCompressLastLevel => ReadConfig<bool>(nameof(OnlyCompressLastLevel));
    public long? MaxWriteBufferSizeToMaintain => ReadConfig<long?>(nameof(MaxWriteBufferSizeToMaintain));
    public bool UseHashSkipListMemtable => ReadConfig<bool>(nameof(UseHashSkipListMemtable));
    public int? BlockRestartInterval => ReadConfig<int?>(nameof(BlockRestartInterval));
    public double MemtablePrefixBloomSizeRatio => ReadConfig<double>(nameof(MemtablePrefixBloomSizeRatio));
    public bool AdviseRandomOnOpen => ReadConfig<bool>(nameof(AdviseRandomOnOpen));
    public bool LevelCompactionDynamicLevelBytes => ReadConfig<bool>(nameof(LevelCompactionDynamicLevelBytes));
    public int? BloomFilterBitsPerKey => ReadConfig<int?>(nameof(BloomFilterBitsPerKey));
    public int? UseRibbonFilterStartingFromLevel => ReadConfig<int?>(nameof(UseRibbonFilterStartingFromLevel));
    public ulong BytesPerSync => ReadConfig<ulong>(nameof(BytesPerSync));
    public double? DataBlockIndexUtilRatio => ReadConfig<double?>(nameof(DataBlockIndexUtilRatio));
    public bool EnableFileWarmer => ReadConfig<bool>(nameof(EnableFileWarmer));

    private T? ReadConfig<T>(string propertyName)
    {
        return ReadConfig<T>(_dbConfig, propertyName, GetPrefix());
    }

    private string GetPrefix()
    {
        return _tableName.StartsWith("State") ? "StateDb" : string.Concat(_tableName, "Db");
    }

    private static T? ReadConfig<T>(IDbConfig dbConfig, string propertyName, string prefix)
    {
        string prefixed = string.Concat(prefix, propertyName);

        try
        {
            Type type = dbConfig.GetType();
            PropertyInfo? propertyInfo = type.GetProperty(prefixed, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            if (propertyInfo is not null && propertyInfo.PropertyType.CanBeAssignedNull())
            {
                // If its nullable check if its null first
                T? val = (T?)propertyInfo?.GetValue(dbConfig);
                if (val is not null)
                {
                    return val;
                }

                // Use generic one even if its available
                propertyInfo = type.GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            }

            // if no custom db property default to generic one
            propertyInfo ??= type.GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            return (T?)propertyInfo?.GetValue(dbConfig);
        }
        catch (Exception e)
        {
            throw new InvalidDataException($"Unable to read {prefixed} property from DB config", e);
        }
    }
}
