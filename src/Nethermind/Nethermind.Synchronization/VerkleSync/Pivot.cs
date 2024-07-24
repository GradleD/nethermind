// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Logging;
using Nethermind.State.Snap;

namespace Nethermind.Synchronization.VerkleSync;


public class PivotChangedEventArgs(long fromBlock, long toBlock) : EventArgs
{
    public long FromBlock { get; } = fromBlock;
    public long ToBlock { get; } = toBlock;
}

public class Pivot
{
    private readonly IBlockTree _blockTree;
    private BlockHeader? _bestHeader;
    private readonly ILogger _logger;

    public event EventHandler<PivotChangedEventArgs>? PivotChanged;

    public long Diff
    {
        get
        {
            return (_blockTree.BestProcessedStatelessHeader?.Number ?? 0) - (_bestHeader?.Number ?? 0);
        }
    }

    public Pivot(IBlockTree blockTree, ILogManager logManager)
    {
        _blockTree = blockTree;
        _logger = logManager.GetClassLogger();
    }

    public BlockHeader GetPivotHeader()
    {
        if (_bestHeader is null || _blockTree.BestProcessedStatelessHeader?.Number - _bestHeader.Number >= Constants.MaxDistanceFromHead - 35)
        {
            BlockHeader? newBestSuggestedHeader = _blockTree.BestProcessedStatelessHeader;
            LogPivotChanged($"distance from HEAD:{Diff}");
            PivotChanged?.Invoke(this, new PivotChangedEventArgs(_bestHeader.Number, newBestSuggestedHeader.Number));
            _bestHeader = newBestSuggestedHeader;
        }

        if (_logger.IsDebug)
        {
            var currentHeader = _blockTree.FindHeader(_bestHeader.Number);
            if (currentHeader.StateRoot != _bestHeader.StateRoot)
            {
                _logger.Warn($"SNAP - Pivot:{_bestHeader.StateRoot}, Current:{currentHeader.StateRoot}");
            }
        }

        return _bestHeader;
    }

    private void LogPivotChanged(string msg)
    {
        _logger.Info($"Snap - {msg} - Pivot changed from {_bestHeader?.Number} to {_blockTree.BestProcessedStatelessHeader?.Number}");
    }

    public void UpdateHeaderForcefully()
    {
        if (_blockTree.BestProcessedStatelessHeader?.Number > _bestHeader.Number)
        {
            BlockHeader? newBestSuggestedHeader = _blockTree.BestProcessedStatelessHeader;
            LogPivotChanged("too many empty responses");
            PivotChanged?.Invoke(this, new PivotChangedEventArgs(_bestHeader.Number, newBestSuggestedHeader.Number));
            _bestHeader = newBestSuggestedHeader;
        }
    }
}
