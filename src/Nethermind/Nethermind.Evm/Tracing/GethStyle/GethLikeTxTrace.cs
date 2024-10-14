// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Nethermind.Core.Crypto;
using Nethermind.Evm.Tracing.GethStyle.Custom;

namespace Nethermind.Evm.Tracing.GethStyle;

[JsonConverter(typeof(GethLikeTxTraceConverter))]
public class GethLikeTxTrace : IDisposable
{
    private readonly IDisposable? _disposable;

    public GethLikeTxTrace(IDisposable? disposable = null)
    {
        _disposable = disposable;
    }

    public GethLikeTxTrace() { }

    public Stack<Dictionary<string, string>> StoragesByDepth { get; } = new();

    public Hash256 TxHash { get; set; }

    public long Gas { get; set; }

    public bool Failed { get; set; }

    public byte[] ReturnValue { get; set; } = Array.Empty<byte>();

    public List<GethTxTraceEntry> Entries { get; set; } = new();

    public GethLikeCustomTrace? CustomTracerResult { get; set; }

    public void Dispose()
    {
        _disposable?.Dispose();
    }
}

public class GethLikeTxTraceResult
// doing this instead of making a change to the above structure because of the ripple effect to other endpoints!
// if that's preferred then all the endpoints responses would have to be changed - prefered option and initial route I took
// before switching to this, as it was time-consuming and would have required a lot of external input from team memberss!
{

    public GethLikeTxTraceResult(GethLikeTxTrace trace)
    {
        Gas = trace.Gas;
        Failed = trace.Failed;
        ReturnValue = trace.ReturnValue;
        Entries = trace.Entries;
        CustomTracerResult = trace.CustomTracerResult;
    }

    public GethLikeTxTraceResult() { }

    // place converter annotation here, zero ripple effect as it's not used by other endpoints (solves item 3 headache)
    public long Gas { get; set; }

    public bool Failed { get; set; }

    // same here add converter to turn "0x" to "" for same reason as the above
    public byte[] ReturnValue { get; set; } = Array.Empty<byte>();

    public List<GethTxTraceEntry> Entries { get; set; } = new();

    public GethLikeCustomTrace? CustomTracerResult { get; set; }
}

public class GethLikeTxTraceFull // name isn't really to be desired might change.
{
    public Hash256 TxHash { get; set; }
    public GethLikeTxTraceResult Result { get; set; }

    public GethLikeTxTraceFull() {}

    public GethLikeTxTraceFull(GethLikeTxTrace trace)
    {
        Result = new (trace);
        TxHash = trace.TxHash;
    }

}
