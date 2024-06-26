// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.IdentityModel.Tokens;
using Nethermind.Abi;
using Nethermind.Blockchain.Contracts;
using Nethermind.Blockchain.Filters;
using Nethermind.Blockchain.Filters.Topics;
using Nethermind.Blockchain.Find;
using Nethermind.Core;
using Nethermind.Facade.Filters;
using Nethermind.Int256;
using Nethermind.Logging;

namespace Nethermind.Merge.AuRa.Shutter.Contracts;

public class SequencerContract : Contract
{
    private readonly ILogFinder _logFinder;
    private readonly AbiEncodingInfo _transactionSubmittedAbi;
    private const long LogScanChunkSize = 16;
    private const int LogScanCutoffChunks = 16;
    private readonly AddressFilter _addressFilter;
    private readonly TopicsFilter _topicsFilter;
    private readonly ILogger _logger;

    public SequencerContract(Address address, ILogFinder logFinder, ILogManager logManager)
        : base(null, address)
    {
        _transactionSubmittedAbi = AbiDefinition.GetEvent(nameof(ISequencerContract.TransactionSubmitted)).GetCallInfo(AbiEncodingStyle.None);
        _addressFilter = new AddressFilter(ContractAddress!);
        _topicsFilter = new SequenceTopicsFilter(new SpecificTopic(_transactionSubmittedAbi.Signature.Hash));
        _logFinder = logFinder;
        _logger = logManager.GetClassLogger();
    }

    public IEnumerable<ISequencerContract.TransactionSubmitted> GetEvents(ulong eon, ulong txPointer, long headBlockNumber)
    {
        BlockParameter end = new(headBlockNumber);

        for (int i = 0; i < LogScanCutoffChunks; i++)
        {
            long startBlockNumber = end.BlockNumber!.Value - LogScanChunkSize;
            BlockParameter start = new(startBlockNumber);
            LogFilter logFilter = new(0, start, end, _addressFilter, _topicsFilter);

            IEnumerable<FilterLog> logs = _logFinder.FindLogs(logFilter);
            
            if (_logger.IsInfo) _logger.Info($"Got {logs.Count()} Shutter logs from blocks {start.BlockNumber!.Value} - {end.BlockNumber!.Value}");

            bool shouldBreak = false;
            foreach (FilterLog log in logs)
            {
                ISequencerContract.TransactionSubmitted tx = ParseTransactionSubmitted(log);

                // if transaction in chunk is before txPointer then don't search further
                if (tx.Eon < eon || tx.TxIndex <= txPointer)
                {
                    shouldBreak = true;
                }

                if (tx.Eon == eon && tx.TxIndex >= txPointer)
                {
                    yield return tx;
                }
            }

            if (shouldBreak)
            {
                yield break;
            }

            end = new BlockParameter(startBlockNumber - 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ISequencerContract.TransactionSubmitted ParseTransactionSubmitted(FilterLog log)
    {
        object[] decodedEvent = AbiEncoder.Decode(AbiEncodingStyle.Packed, _transactionSubmittedAbi.Signature, log.Data);
        return new ISequencerContract.TransactionSubmitted
        {
            Eon = (ulong)decodedEvent[0],
            TxIndex = (ulong)decodedEvent[1],
            IdentityPrefix = new Bytes32((byte[])decodedEvent[2]),
            Sender = (Address)decodedEvent[3],
            EncryptedTransaction = (byte[])decodedEvent[4],
            GasLimit = (UInt256)decodedEvent[5]
        };
    }
}
