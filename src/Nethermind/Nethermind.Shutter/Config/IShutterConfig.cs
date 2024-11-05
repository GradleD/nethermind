// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.IO;
using System.Linq;
using Multiformats.Address;
using Nethermind.Config;
using Nethermind.Core;

namespace Nethermind.Shutter.Config;

public interface IShutterConfig : IConfig
{
    [ConfigItem(Description = "Whether to enable Shutter.", DefaultValue = "false")]
    bool Enabled { get; set; }

    [ConfigItem(Description = "The filepath of the validator info json file.",
        DefaultValue = "null")]
    string? ValidatorInfoFile { get; set; }

    [ConfigItem(Description = "The address of the Shutter sequencer contract.",
        DefaultValue = "null")]
    string? SequencerContractAddress { get; set; }

    [ConfigItem(Description = "The address of the Shutter validator registry contract.",
        DefaultValue = "null")]
    string? ValidatorRegistryContractAddress { get; set; }

    [ConfigItem(Description = "The address of the Shutter key broadcast contract.",
        DefaultValue = "null")]
    string? KeyBroadcastContractAddress { get; set; }

    [ConfigItem(Description = "The address of the Shutter keyper set manager contract.",
        DefaultValue = "null")]
    string? KeyperSetManagerContractAddress { get; set; }

    [ConfigItem(Description = "The p2p addresses of the Shutter Keyper network bootnodes.",
        DefaultValue = null)]
    string[]? BootnodeP2PAddresses { get; set; }

    [ConfigItem(Description = "Instance ID of Shutter keyper set.",
        DefaultValue = "0")]
    ulong InstanceID { get; set; }

    [ConfigItem(Description = "The port to connect to Shutter P2P network with.",
        DefaultValue = "23102")]
    int P2PPort { get; set; }

    [ConfigItem(Description = "The Shutter P2P protocol version.",
        DefaultValue = "/shutter/0.1.0", HiddenFromDocs = true)]
    string? P2PProtocolVersion { get; set; }

    [ConfigItem(Description = "The Shutter P2P agent version.",
        DefaultValue = "github.com/shutter-network/rolling-shutter/rolling-shutter",
        HiddenFromDocs = true)]
    string? P2PAgentVersion { get; set; }

    [ConfigItem(Description = "The filename to use for the Shutter P2P key. If this not specified, the key is autogenerated in `shutter.key.plain` file.")]
    string ShutterKeyFile { get; set; }

    [ConfigItem(Description = "The Shutter validator registry message version.",
        DefaultValue = "1", HiddenFromDocs = true)]
    ulong ValidatorRegistryMessageVersion { get; set; }

    [ConfigItem(Description = "The maximum amount of gas to use on Shutter transactions.",
        DefaultValue = "10000000", HiddenFromDocs = true)]
    int EncryptedGasLimit { get; set; }

    [ConfigItem(Description = "Maximum amount of milliseconds into the slot to wait for Shutter keys before building block.",
        DefaultValue = "1666", HiddenFromDocs = true)]
    ushort MaxKeyDelay { get; }

    [ConfigItem(Description = "Whether to build Shutter blocks or just give metrics on Shutter transactions.",
        DefaultValue = "true", HiddenFromDocs = true)]
    bool Validator { get; set; }

    [ConfigItem(Description = "How many minutes to wait for transactions before sending a disconnection warning.",
        DefaultValue = "20", HiddenFromDocs = true)]
    ushort DisconnectionLogTimeout { get; set; }

    public void Validate(out Multiaddress[] bootnodeP2PAddresses)
    {
        if (Validator && ValidatorInfoFile is null)
        {
            throw new ArgumentException($"Must set Shutter.ValidatorInfoFile to a valid json file.");
        }

        if (ValidatorInfoFile is not null && !File.Exists(ValidatorInfoFile))
        {
            throw new ArgumentException($"Shutter validator info file \"{ValidatorInfoFile}\" does not exist.");
        }

        if (SequencerContractAddress is null || !Address.TryParse(SequencerContractAddress, out _))
        {
            throw new ArgumentException("Must set Shutter sequencer contract address to valid address.");
        }

        if (ValidatorRegistryContractAddress is null || !Address.TryParse(ValidatorRegistryContractAddress, out _))
        {
            throw new ArgumentException("Must set Shutter validator registry contract address to valid address.");
        }

        if (KeyBroadcastContractAddress is null || !Address.TryParse(KeyBroadcastContractAddress, out _))
        {
            throw new ArgumentException("Must set Shutter key broadcast contract address to valid address.");
        }

        if (KeyperSetManagerContractAddress is null || !Address.TryParse(KeyperSetManagerContractAddress, out _))
        {
            throw new ArgumentException("Must set Shutter keyper set manager contract address to valid address.");
        }

        if (DisconnectionLogTimeout == 0)
        {
            throw new ArgumentException("Must set Shutter disconnection log timeout greater than 0.");
        }

        if (P2PAgentVersion is null)
        {
            throw new ArgumentNullException(nameof(P2PAgentVersion));
        }

        if (P2PProtocolVersion is null)
        {
            throw new ArgumentNullException(nameof(P2PProtocolVersion));
        }

        if (BootnodeP2PAddresses is null)
        {
            throw new ArgumentNullException(nameof(BootnodeP2PAddresses));
        }

        try
        {
            bootnodeP2PAddresses = BootnodeP2PAddresses.Select(addr => Multiaddress.Decode(addr)).ToArray();
        }
        catch (NotSupportedException e)
        {
            throw new ArgumentException($"Could not decode Shutter bootnode p2p addresses.", e);
        }
    }
}
