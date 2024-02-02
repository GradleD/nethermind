// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Blockchain.ValidatorExit;
using Nethermind.Core.Test.Builders;
using Nethermind.Serialization.Rlp;
using NUnit.Framework;

namespace Nethermind.Core.Test.Encoding;

[TestFixture]
public class ValidatorExitDecoderTests
{
    private static ValidatorExitsDecoder _decoder = new();

    [Test]
    public void Roundtrip()
    {
        byte[] validatorPubkey = new byte[48];
        validatorPubkey[11] = 11;
        ValidatorExit exit = new(TestItem.AddressA, validatorPubkey);

        Rlp encoded = _decoder.Encode(exit);
        ValidatorExit decoded = _decoder.Decode(encoded.Bytes);

        Assert.That(decoded.SourceAddress, Is.EqualTo(TestItem.AddressA), "sourceAddress");
        Assert.That(decoded.ValidatorPubkey, Is.EqualTo(validatorPubkey), "validatorPubKey");
    }

    [Test]
    public void GetLength_should_be_72()
    {
        byte[] validatorPubkey = new byte[48];
        ValidatorExit exit = new(TestItem.AddressA, validatorPubkey);
        Assert.That(_decoder.GetLength(exit, RlpBehaviors.None), Is.EqualTo(72), "GetLength");
    }
}
