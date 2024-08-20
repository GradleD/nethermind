// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using NUnit.Framework;

namespace Nethermind.Shutter.Test;

[TestFixture]
class ShutterMessageHandlerTests
{
    [Test]
    public void Can_accept_valid_decryption_keys()
    {
        ShutterMessageHandler msgHandler = ShutterTestsCommon.InitMessageHandler();
        bool eventFired = false;
        msgHandler.KeysValidated += (_, _) => eventFired = true;
        msgHandler.OnDecryptionKeysReceived(new Dto.DecryptionKeys()
        {

        });
        Assert.That(eventFired);
    }

    [Test]
    public void Can_reject_invalid_decryption_keys()
    {
        ShutterMessageHandler msgHandler = ShutterTestsCommon.InitMessageHandler();
        bool eventFired = false;
        msgHandler.KeysValidated += (_, _) => eventFired = true;
        msgHandler.OnDecryptionKeysReceived(new Dto.DecryptionKeys()
        {

        });
        Assert.That(!eventFired);
    }

    [Test]
    public void Can_reject_outdated_decryption_keys()
    {
        ShutterMessageHandler msgHandler = ShutterTestsCommon.InitMessageHandler();
        bool eventFired = false;
        msgHandler.KeysValidated += (_, _) => eventFired = true;
        msgHandler.OnDecryptionKeysReceived(new Dto.DecryptionKeys()
        {

        });
        Assert.That(!eventFired);
    }

}
