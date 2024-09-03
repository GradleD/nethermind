// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.ComponentModel;
using Nethermind.Core.Attributes;

namespace Nethermind.Shutter;

public class Metrics
{
    [CounterMetric]
    [Description("Number of keys not received.")]
    public static ulong KeysMissed { get; set; }

    [Description("Eon of the latest block.")]
    public static ulong Eon { get; set; }

    [Description("Size of keyper set in current eon.")]
    public static int Keypers { get; set; }

    [Description("Number of keypers assumed to be honest and online for current eon.")]
    public static int Threshold { get; set; }

    [Description("Number of transactions since Shutter genesis.")]
    public static ulong TxPointer { get; set; }

    [GaugeMetric]
    [Description("Relative time offset (ms) from slot boundary that keys were received.")]
    public static long KeysReceivedTimeOffset { get; set; }

    [GaugeMetric]
    [Description("Number of transactions included.")]
    public static uint Transactions { get; set; }

    [GaugeMetric]
    [Description("Number of invalid transactions that could not be included.")]
    public static uint BadTransactions { get; set; }

    [GaugeMetric]
    [Description("Amount of encrypted gas used.")]
    public static ulong EncryptedGasUsed { get; set; }
}