// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Diagnostics;
using Lantern.Discv5.Enr;
using Nethermind.Core.Extensions;
using Nethermind.Logging;
using Nethermind.Network.Discovery.Kademlia;
using Nethermind.Network.Discovery.Kademlia.Content;
using Nethermind.Network.Discovery.Portal.Messages;

namespace Nethermind.Network.Discovery.Portal;

public class ContentLookupService(
    ContentNetworkConfig config,
    IUtpManager utpManager,
    IKademliaContent<byte[], LookupContentResult> kademliaContent,
    IContentNetworkProtocol protocol,
    IEnrProvider enrProvider,
    ILogManager logManager
) {
    private readonly TimeSpan _lookupContentHardTimeout = config.LookupContentHardTimeout;
    private readonly ILogger _logger1 = logManager.GetClassLogger<PortalContentNetwork>();

    public async Task<(byte[]? Payload, bool UtpTransfer)> LookupContent(byte[] key, CancellationToken token)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(_lookupContentHardTimeout);
        token = cts.Token;

        Stopwatch sw = Stopwatch.StartNew();
        var result = await kademliaContent.LookupValue(key, token);
        _logger1.Info($"Lookup {key.ToHexString()} took {sw.Elapsed}");

        sw.Restart();

        if (result == null) return (null, false);

        if (result.Payload != null) return (result.Payload, false);

        Debug.Assert(result.ConnectionId != null);

        MemoryStream stream = new MemoryStream();
        await utpManager.ReadContentFromUtp(result.NodeId, true, result.ConnectionId.Value, stream, token);
        var asBytes = stream.ToArray();
        _logger1.Info($"UTP download for {key.ToHexString()} took {sw.Elapsed}");
        return (asBytes, true);
    }

    public async Task<(byte[]? Payload, bool UtpTransfer, IEnr[]? Neighbours)> LookupContentFrom(IEnr node, byte[] contentKey, CancellationToken token)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(_lookupContentHardTimeout);
        token = cts.Token;

        Content value = await protocol.FindContent(node, new FindContent()
        {
            ContentKey = new ContentKey()
            {
                Data = contentKey
            },
        }, token);

        if (value.Selector == ContentType.Enrs)
        {
            IEnr[] enrs = value.Enrs!.Select((enr) => enrProvider.Decode(enr.Data)).ToArray();
            return (null, false, enrs);
        }

        if (value.Selector == ContentType.Payload) return (value.Payload, false, null);

        MemoryStream stream = new MemoryStream();
        await utpManager.ReadContentFromUtp(node, true, value.ConnectionId!, stream, token);
        var asBytes = stream.ToArray();
        return (asBytes, true, null);
    }
}
