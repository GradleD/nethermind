// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;
using Nethermind.Core;
using Nethermind.Crypto;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Serialization.Rlp;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Core.Test.Builders;
using FluentAssertions;
using Nethermind.State;
using Nethermind.Core.Specs;
using Nethermind.Evm.CodeAnalysis;
using Nethermind.Core.Extensions;
using Nethermind.Db;
using Nethermind.Trie.Pruning;

namespace Nethermind.Evm.Test;

[TestFixture]
public class CodeInfoRepositoryTests
{
    [Test]
    public void InsertFromAuthorizations_AuthorityTupleIsCorrect_CodeIsInserted()
    {
        PrivateKey authority = TestItem.PrivateKeyA;
        CodeInfoRepository sut = new(1);
        var tuples = new[]
        {
            CreateAuthorizationTuple(authority, 1, TestItem.AddressB, 0),
        };
        HashSet<Address> accessedAddresses = new();
        int result = sut.InsertFromAuthorizations(Substitute.For<IWorldState>(), tuples, accessedAddresses, Substitute.For<IReleaseSpec>());

        accessedAddresses.Should().BeEquivalentTo([authority.Address]);
    }

    public static IEnumerable<object[]> AuthorizationCases()
    {
        yield return new object[]
        {
            CreateAuthorizationTuple(TestItem.PrivateKeyA, 1, TestItem.AddressB, 0),
            true
        };
        yield return new object[]
        {
            //Wrong chain id
            CreateAuthorizationTuple(TestItem.PrivateKeyB, 2, TestItem.AddressB, 0),
            false
        };
        yield return new object[]
        {            
            //wrong nonce
            CreateAuthorizationTuple(TestItem.PrivateKeyC, 1, TestItem.AddressB, 1),
            false
        };
    }

    [TestCaseSource(nameof(AuthorizationCases))]
    public void InsertFromAuthorizations_MixOfCorrectAndWrongChainIdAndNonce_InsertsIfExpected(AuthorizationTuple tuple, bool shouldInsert)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        CodeInfoRepository sut = new(1);
        HashSet<Address> accessedAddresses = new();
        sut.InsertFromAuthorizations(stateProvider, [tuple], accessedAddresses, Substitute.For<IReleaseSpec>());

        Assert.That(stateProvider.HasCode(tuple.Authority), Is.EqualTo(shouldInsert));
    }

    [Test]
    public void InsertFromAuthorizations_AuthorityHasCode_NoCodeIsInserted()
    {
        PrivateKey authority = TestItem.PrivateKeyA;
        Address codeSource = TestItem.AddressB;
        IWorldState mockWorldState = Substitute.For<IWorldState>();
        mockWorldState.HasCode(authority.Address).Returns(true);
        mockWorldState.GetCode(authority.Address).Returns(new byte[32]);
        CodeInfoRepository sut = new(1);
        var tuples = new[]
        {
            CreateAuthorizationTuple(authority, 1, codeSource, 0),
        };
        HashSet<Address> accessedAddresses = new();

        sut.InsertFromAuthorizations(mockWorldState, tuples, accessedAddresses, Substitute.For<IReleaseSpec>());

        mockWorldState.DidNotReceive().InsertCode(Arg.Any<Address>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<IReleaseSpec>());
    }

    [Test]
    public void InsertFromAuthorizations_AuthorityHasDelegatedCode_CodeIsInserted()
    {
        PrivateKey authority = TestItem.PrivateKeyA;
        Address codeSource = TestItem.AddressB;
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        byte[] code = new byte[23];
        Eip7702Constants.DelegationHeader.CopyTo(code);
        stateProvider.CreateAccount(authority.Address, 0);
        stateProvider.InsertCode(authority.Address, Keccak.Compute(code), code, Substitute.For<IReleaseSpec>());
        CodeInfoRepository sut = new(1);
        var tuples = new[]
        {
            CreateAuthorizationTuple(authority, 1, codeSource, 0),
        };

        sut.InsertFromAuthorizations(stateProvider, tuples, Substitute.For<ISet<Address>>(), Substitute.For<IReleaseSpec>());

        Assert.That(stateProvider.GetCode(authority.Address).Slice(3), Is.EqualTo(codeSource.Bytes));
    }

    [Test]
    public void InsertFromAuthorizations_AuthorityHasZeroNonce_NonceIsIncrementedByOne()
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        PrivateKey authority = TestItem.PrivateKeyA;
        Address codeSource = TestItem.AddressB;
        stateProvider.CreateAccount(authority.Address, 0);
        CodeInfoRepository sut = new(1);
        var tuples = new[]
        {
            CreateAuthorizationTuple(authority, 1, codeSource, 0),
        };

        sut.InsertFromAuthorizations(stateProvider, tuples, Substitute.For<ISet<Address>>(), Substitute.For<IReleaseSpec>());

        Assert.That(stateProvider.GetNonce(authority.Address), Is.EqualTo((UInt256)1));
    }

    [Test]
    public void InsertFromAuthorizations_FourAuthorizationInTotalButOneHasInvalidNonce_ResultContainsThreeAddresses()
    {
        CodeInfoRepository sut = new(1);
        var tuples = new[]
        {
            CreateAuthorizationTuple(TestItem.PrivateKeyA, 1, TestItem.AddressF, 0),
            CreateAuthorizationTuple(TestItem.PrivateKeyB, 1, TestItem.AddressF, 0),
            CreateAuthorizationTuple(TestItem.PrivateKeyC, 2, TestItem.AddressF, 0),
            CreateAuthorizationTuple(TestItem.PrivateKeyD, 1, TestItem.AddressF, 1),
        };
        HashSet<Address> addresses = new();
        sut.InsertFromAuthorizations(Substitute.For<IWorldState>(), tuples, addresses, Substitute.For<IReleaseSpec>());

        addresses.Should().BeEquivalentTo([TestItem.AddressA, TestItem.AddressB, TestItem.AddressD]);
    }

    [Test]
    public void InsertFromAuthorizations_AuthorizationsHasOneExistingAccount_ResultHaveOneRefund()
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        CodeInfoRepository sut = new(1);
        var tuples = new[]
        {
            CreateAuthorizationTuple(TestItem.PrivateKeyA, 1, TestItem.AddressF, 0),
            CreateAuthorizationTuple(TestItem.PrivateKeyB, 1, TestItem.AddressF, 0),
        };
        stateProvider.CreateAccount(TestItem.AddressA, 0);

        int refunds = sut.InsertFromAuthorizations(stateProvider, tuples, Substitute.For<ISet<Address>>(), Substitute.For<IReleaseSpec>());

        refunds.Should().Be(1);
    }
    public static IEnumerable<object[]> CountsAsAccessedCases()
    {
        yield return new object[]
        {
            new AuthorizationTuple[]
            {
                CreateAuthorizationTuple(TestItem.PrivateKeyA, 1, TestItem.AddressF, 0),
                CreateAuthorizationTuple(TestItem.PrivateKeyB, 1, TestItem.AddressF, 0),
            },
            new Address[]
            {
                TestItem.AddressA,
                TestItem.AddressB
            }
        };
        yield return new object[]
        {
            new AuthorizationTuple[]
            {
                CreateAuthorizationTuple(TestItem.PrivateKeyA, 1, TestItem.AddressF, 0),
                CreateAuthorizationTuple(TestItem.PrivateKeyB, 2, TestItem.AddressF, 0),
            },
            new Address[]
            {
                TestItem.AddressA,
            }
        };
        yield return new object[]
        {
            new AuthorizationTuple[]
            {
                CreateAuthorizationTuple(TestItem.PrivateKeyA, 1, TestItem.AddressF, 0),
                //Bad signature
                new AuthorizationTuple(1, TestItem.AddressF, 0, new Signature(new byte[65]), TestItem.AddressA)
            },
            new Address[]
            {
                TestItem.AddressA,
            }
        };
    }

    [TestCaseSource(nameof(CountsAsAccessedCases))]
    public void InsertFromAuthorizations_TwoValidAuthorizations_(AuthorizationTuple[] tuples, Address[] shouldCountAsAccessed)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        CodeInfoRepository sut = new(1);
        stateProvider.CreateAccount(TestItem.AddressA, 0);

        ISet<Address> accessedAddresses = new HashSet<Address>();
        sut.InsertFromAuthorizations(stateProvider, tuples, accessedAddresses, Substitute.For<IReleaseSpec>());

        accessedAddresses.Count.Should().Be(shouldCountAsAccessed.Length);
        accessedAddresses.Should().Contain(shouldCountAsAccessed);
    }

    public static IEnumerable<object[]> NotDelegationCodeCases()
    {
        byte[] rndAddress = new byte[20];
        TestContext.CurrentContext.Random.NextBytes(rndAddress);
        //Change first byte of the delegation header
        byte[] code = [.. Eip7702Constants.DelegationHeader, .. rndAddress];
        code[0] = TestContext.CurrentContext.Random.NextByte(0xee);
        yield return new object[]
        {
            code
        };
        //Change second byte of the delegation header
        code = [.. Eip7702Constants.DelegationHeader, .. rndAddress];
        code[1] = TestContext.CurrentContext.Random.NextByte(0x2, 0xff);
        yield return new object[]
        {
            code
        };
        //Change third byte of the delegation header
        code = [.. Eip7702Constants.DelegationHeader, .. rndAddress];
        code[2] = TestContext.CurrentContext.Random.NextByte(0x1, 0xff);
        yield return new object[]
        {
            code
        };
        code = [.. Eip7702Constants.DelegationHeader, .. new byte[21]];
        yield return new object[]
        {
            code
        };
        code = [.. Eip7702Constants.DelegationHeader, .. new byte[19]];
        yield return new object[]
        {
            code
        };
    }
    [TestCaseSource(nameof(NotDelegationCodeCases))]
    public void IsDelegation_CodeIsNotDelegation_ReturnsFalse(byte[] code)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        stateProvider.CreateAccount(TestItem.AddressA, 0);
        stateProvider.InsertCode(TestItem.AddressA, code, Substitute.For<IReleaseSpec>());
        CodeInfoRepository sut = new(1);

        sut.IsDelegation(stateProvider, TestItem.AddressA, out _).Should().Be(false);
    }


    public static IEnumerable<object[]> DelegationCodeCases()
    {
        byte[] address = new byte[20];
        byte[] code = [.. Eip7702Constants.DelegationHeader, .. address];
        yield return new object[]
        {
            code
        };
        TestContext.CurrentContext.Random.NextBytes(address);
        code = [.. Eip7702Constants.DelegationHeader, .. address];
        yield return new object[]
        {
            code
        };
    }
    [TestCaseSource(nameof(DelegationCodeCases))]
    public void IsDelegation_CodeIsDelegation_ReturnsTrue(byte[] code)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        stateProvider.CreateAccount(TestItem.AddressA, 0);
        stateProvider.InsertCode(TestItem.AddressA, code, Substitute.For<IReleaseSpec>());
        CodeInfoRepository sut = new(1);

        sut.IsDelegation(stateProvider, TestItem.AddressA, out _).Should().Be(true);
    }

    [TestCaseSource(nameof(DelegationCodeCases))]
    public void IsDelegation_CodeIsDelegation_CorrectDelegationAddressIsSet(byte[] code)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        stateProvider.CreateAccount(TestItem.AddressA, 0);
        stateProvider.InsertCode(TestItem.AddressA, code, Substitute.For<IReleaseSpec>());
        CodeInfoRepository sut = new(1);

        Address result;
        sut.IsDelegation(stateProvider, TestItem.AddressA, out result);

        result.Should().Be(new Address(code.Slice(3, Address.Size)));
    }

    [TestCaseSource(nameof(DelegationCodeCases))]
    public void GetCodeHash_CodeIsDelegation_ReturnsHashOfDelegated(byte[] code)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        stateProvider.CreateAccount(TestItem.AddressA, 0);
        stateProvider.InsertCode(TestItem.AddressA, code, Substitute.For<IReleaseSpec>());
        Address delegationAddress = new Address(code.Slice(3, Address.Size));
        byte[] delegationCode = new byte[32];
        stateProvider.CreateAccount(delegationAddress, 0);
        stateProvider.InsertCode(delegationAddress, delegationCode, Substitute.For<IReleaseSpec>());

        CodeInfoRepository sut = new(1);

        sut.GetCodeHash(stateProvider, TestItem.AddressA).Should().Be(Keccak.Compute(delegationCode).ValueHash256);
    }

    [TestCaseSource(nameof(NotDelegationCodeCases))]
    public void GetCodeHash_CodeIsNotDelegation_ReturnsCodeHashOfAddress(byte[] code)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        stateProvider.CreateAccount(TestItem.AddressA, 0);
        stateProvider.InsertCode(TestItem.AddressA, code, Substitute.For<IReleaseSpec>());

        CodeInfoRepository sut = new(1);

        sut.GetCodeHash(stateProvider, TestItem.AddressA).Should().Be(Keccak.Compute(code).ValueHash256);
    }

    [TestCaseSource(nameof(DelegationCodeCases))]
    public void GetCachedCodeInfo_CodeIsDelegation_ReturnsCodeOfDelegation(byte[] code)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        stateProvider.CreateAccount(TestItem.AddressA, 0);
        stateProvider.InsertCode(TestItem.AddressA, code, Substitute.For<IReleaseSpec>());
        Address delegationAddress = new Address(code.Slice(3, Address.Size));
        stateProvider.CreateAccount(delegationAddress, 0);
        byte[] delegationCode = new byte[32];
        stateProvider.InsertCode(delegationAddress, delegationCode, Substitute.For<IReleaseSpec>());
        CodeInfoRepository sut = new(1);

        CodeInfo result = sut.GetCachedCodeInfo(stateProvider, TestItem.AddressA, Substitute.For<IReleaseSpec>());
        result.MachineCode.ToArray().Should().BeEquivalentTo(delegationCode);
    }

    [TestCaseSource(nameof(NotDelegationCodeCases))]
    public void GetCachedCodeInfo_CodeIsNotDelegation_ReturnsCodeOfAddress(byte[] code)
    {
        IDb stateDb = new MemDb();
        IDb codeDb = new MemDb();
        TrieStore trieStore = new(stateDb, LimboLogs.Instance);
        IWorldState stateProvider = new WorldState(trieStore, codeDb, LimboLogs.Instance);
        stateProvider.CreateAccount(TestItem.AddressA, 0);
        stateProvider.InsertCode(TestItem.AddressA, code, Substitute.For<IReleaseSpec>());

        CodeInfoRepository sut = new(1);

        sut.GetCachedCodeInfo(stateProvider, TestItem.AddressA, Substitute.For<IReleaseSpec>()).Should().BeEquivalentTo(new CodeInfo(code));
    }

    private static AuthorizationTuple CreateAuthorizationTuple(PrivateKey signer, ulong chainId, Address codeAddress, ulong nonce)
    {
        AuthorizationTupleDecoder decoder = new();
        RlpStream rlp = decoder.EncodeWithoutSignature(chainId, codeAddress, nonce);
        Span<byte> code = stackalloc byte[rlp.Length + 1];
        code[0] = Eip7702Constants.Magic;
        rlp.Data.AsSpan().CopyTo(code.Slice(1));
        EthereumEcdsa ecdsa = new(1);
        Signature sig = ecdsa.Sign(signer, Keccak.Compute(code));

        return new AuthorizationTuple(chainId, codeAddress, nonce, sig, signer.Address);
    }
}
