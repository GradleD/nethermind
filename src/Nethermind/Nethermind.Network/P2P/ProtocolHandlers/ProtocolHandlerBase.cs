// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetty.Buffers;
using Nethermind.Core;
using Nethermind.Logging;
using Nethermind.Network.P2P.EventArg;
using Nethermind.Network.P2P.Messages;
using Nethermind.Network.Rlpx;
using Nethermind.Serialization.Rlp;
using Nethermind.Stats;
using Nethermind.Stats.Model;

namespace Nethermind.Network.P2P.ProtocolHandlers
{
    public abstract class ProtocolHandlerBase : IProtocolHandler
    {
        public abstract string Name { get; }
        public bool IsPriority { get; set; }
        protected INodeStatsManager StatsManager { get; }
        private readonly IMessageSerializationService _serializer;
        protected ISession Session { get; }
        protected long Counter;

        private readonly TaskCompletionSource<MessageBase> _initCompletionSource;

        protected ProtocolHandlerBase(ISession session, INodeStatsManager nodeStats, IMessageSerializationService serializer, ILogManager logManager)
        {
            Logger = logManager?.GetClassLogger(GetType()) ?? throw new ArgumentNullException(nameof(logManager));
            StatsManager = nodeStats ?? throw new ArgumentNullException(nameof(nodeStats));
            Session = session ?? throw new ArgumentNullException(nameof(session));

            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _initCompletionSource = new TaskCompletionSource<MessageBase>();
        }

        protected ILogger Logger { get; }

        protected abstract TimeSpan InitTimeout { get; }

        protected T Deserialize<T>(byte[] data) where T : P2PMessage
        {
            try
            {
                return _serializer.Deserialize<T>(data);
            }
            catch (RlpException e)
            {
                if (Logger.IsDebug) Logger.Debug($"Failed to deserialize message {typeof(T).Name}, with exception {e}");
                ReportIn($"{typeof(T).Name} - Deserialization exception");
                throw;
            }
        }

        protected T Deserialize<T>(IByteBuffer data) where T : P2PMessage
        {
            try
            {
                return _serializer.Deserialize<T>(data);
            }
            catch (RlpException e)
            {
                if (Logger.IsDebug) Logger.Debug($"Failed to deserialize message {typeof(T).Name}, with exception {e}");
                ReportIn($"{typeof(T).Name} - Deserialization exception");
                throw;
            }
        }

        protected void Send<T>(T message) where T : P2PMessage
        {
            Interlocked.Increment(ref Counter);
            if (Logger.IsTrace) Logger.Trace($"{Counter} Sending {typeof(T).Name}");
            if (NetworkDiagTracer.IsEnabled) NetworkDiagTracer.ReportOutgoingMessage(Session.Node?.Address, Name, message.ToString());
            Session.DeliverMessage(message);
        }

        protected async Task CheckProtocolInitTimeout()
        {
            Task<MessageBase> receivedInitMsgTask = _initCompletionSource.Task;
            CancellationTokenSource delayCancellation = new();
            Task firstTask = await Task.WhenAny(receivedInitMsgTask, Task.Delay(InitTimeout, delayCancellation.Token));

            if (firstTask != receivedInitMsgTask)
            {
                if (Logger.IsTrace)
                {
                    Logger.Trace($"Disconnecting due to timeout for protocol init message ({Name}): {Session.RemoteNodeId}");
                }

                Session.InitiateDisconnect(InitiateDisconnectReason.ProtocolInitTimeout, "protocol init timeout");
            }
            else
            {
                delayCancellation.Cancel();
            }
        }

        protected void ReceivedProtocolInitMsg(MessageBase msg)
        {
            _initCompletionSource?.SetResult(msg);
        }

        protected void ReportIn(MessageBase msg)
        {
            if (Logger.IsTrace || NetworkDiagTracer.IsEnabled)
            {
                ReportIn(msg.ToString());
            }
        }

        protected void ReportIn(string messageInfo)
        {
            if (Logger.IsTrace)
                Logger.Trace($"OUT {Counter:D5} {messageInfo}");

            if (NetworkDiagTracer.IsEnabled)
                NetworkDiagTracer.ReportIncomingMessage(Session?.Node?.Address, Name, messageInfo);
        }

        public abstract void Dispose();

        public abstract byte ProtocolVersion { get; }

        public abstract string ProtocolCode { get; }

        public abstract int MessageIdSpaceSize { get; }

        public abstract void Init();

        public abstract void HandleMessage(Packet message);

        public abstract void DisconnectProtocol(DisconnectReason disconnectReason, string details);

        public abstract event EventHandler<ProtocolInitializedEventArgs> ProtocolInitialized;

        public abstract event EventHandler<ProtocolEventArgs> SubprotocolRequested;
    }
}
