/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nethermind.Core.Logging;
using Nethermind.Network.Config;
using Nethermind.Network.P2P;
using Nethermind.Stats.Model;

namespace Nethermind.Network
{
    public class SessionMonitor : ISessionMonitor
    {
        private System.Timers.Timer _pingTimer;
        
        private readonly INetworkConfig _networkConfig;
        private readonly ILogger _logger;

        public SessionMonitor(INetworkConfig config, ILogManager logManager)
        {
            _networkConfig = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logManager.GetClassLogger() ?? throw new ArgumentNullException(nameof(logManager));
        }

        public void Start()
        {
            StartPingTimer();
        }

        public async Task StopAsync()
        {
            StopPingTimer();
            await Task.CompletedTask;
        }

        private List<IP2PSession> _sessions = new List<IP2PSession>();
        
        public void AddSession(IP2PSession session)
        {
            lock (_sessions)
            {
                session.Disconnected += OnDisconnected;
                if (session.SessionState < SessionState.DisconnectingProtocols)
                {
                    _sessions.Add(session);
                }
            }
        }

        private void OnDisconnected(object sender, DisconnectEventArgs e)
        {
            lock (_sessions)
            {
                _sessions.Remove((IP2PSession)sender);
            }
        }

        private void SendPingMessages()
        {
            var task = Task.Run(SendPingMessagesAsync).ContinueWith(x =>
            {
                if (x.IsFaulted && _logger.IsError)
                {
                    _logger.Error($"Error during send ping messages: {x.Exception}");
                }
            });
            task.Wait();
        }

        private async Task SendPingMessagesAsync()
        {
            var pingTasks = new List<(IP2PSession session, Task<bool> pingTask)>();
            lock (_sessions)
            {
                foreach (var session in _sessions)
                {
                    if (session.SessionState == SessionState.Initialized)
                    {
                        var pingTask = SendPingMessage(session);
                        pingTasks.Add((session, pingTask));
                    }
                }
            }

            if (pingTasks.Any())
            {
                var tasks = await Task.WhenAll(pingTasks.Select(x => x.pingTask));
                if (_logger.IsTrace) _logger.Trace($"Sent ping messages to {tasks.Length} peers. Disconnected: {tasks.Count(x => x == false)}");
            }
            else if (_logger.IsTrace) _logger.Trace("Sent no ping messages.");
        }

        private async Task<bool> SendPingMessage(IP2PSession session)
        {
            if (session.PingSender == null)
            {
                /* this would happen when session is initialized already but the protocol is not yet initialized
                   we do not have a separate session state for it at the moment */
                return true;
            }

            if (session.IsClosing)
            {
                return true;
            }
            
            for (var i = 0; i < _networkConfig.P2PPingRetryCount; i++)
            {
                var pongReceived = await session.PingSender.SendPing();
                if (pongReceived)
                {
                    return true;
                }
            }

            if (_logger.IsTrace) _logger.Trace($"Disconnecting due to missed ping messages: {session.RemoteNodeId}");
            session.InitiateDisconnect(DisconnectReason.ReceiveMessageTimeout);
            return false;
        }

        private void StartPingTimer()
        {
            if (_logger.IsDebug) _logger.Debug("Starting ping timer");

            _pingTimer = new System.Timers.Timer(_networkConfig.P2PPingInterval) {AutoReset = false};
            _pingTimer.Elapsed += (sender, e) =>
            {
                try
                {
                    _pingTimer.Enabled = false;
                    SendPingMessages();
                }
                catch (Exception exception)
                {
                    if (_logger.IsDebug) _logger.Error("Ping timer failed", exception);
                }
                finally
                {
                    _pingTimer.Enabled = true;
                }
            };

            _pingTimer.Start();
        }

        private void StopPingTimer()
        {
            try
            {
                if (_logger.IsDebug) _logger.Debug("Stopping ping timer");
                _pingTimer?.Stop();
            }
            catch (Exception e)
            {
                _logger.Error("Error during ping timer stop", e);
            }
        }
    }
}