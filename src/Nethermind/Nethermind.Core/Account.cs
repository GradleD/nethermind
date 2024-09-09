// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.Core
{
    public class Account : IEquatable<Account>
    {
        public static readonly Account TotallyEmpty = new();

        private readonly Hash256? _codeHash;
        private readonly Hash256? _storageRoot;
        private readonly UInt256 _nonce;
        private readonly UInt256 _balance;

        public Account(in UInt256 balance)
        {
            _codeHash = null;
            _storageRoot = null;
            _nonce = default;
            _balance = balance;
        }

        public Account(in UInt256 nonce, in UInt256 balance)
        {
            _codeHash = null;
            _storageRoot = null;
            _nonce = nonce;
            _balance = balance;
        }

        private Account()
        {
            _codeHash = null;
            _storageRoot = null;
            _nonce = default;
            _balance = default;
        }

        public Account(in UInt256 nonce, in UInt256 balance, Hash256 storageRoot, Hash256 codeHash)
        {
            _codeHash = codeHash == Keccak.OfAnEmptyString ? null : codeHash;
            _storageRoot = storageRoot == Keccak.EmptyTreeHash ? null : storageRoot;
            _nonce = nonce;
            _balance = balance;
        }

        private Account(Account account, Hash256? storageRoot)
        {
            _codeHash = account._codeHash;
            _storageRoot = storageRoot == Keccak.EmptyTreeHash ? null : storageRoot;
            _nonce = account._nonce;
            _balance = account._balance;
        }

        private Account(Hash256? codeHash, Account account)
        {
            _codeHash = codeHash == Keccak.OfAnEmptyString ? null : codeHash;
            _storageRoot = account._storageRoot;
            _nonce = account._nonce;
            _balance = account._balance;
        }

        private Account(Account account, in UInt256 nonce, in UInt256 balance)
        {
            _codeHash = account._codeHash;
            _storageRoot = account._storageRoot;
            _nonce = nonce;
            _balance = balance;
        }

        public bool HasCode => _codeHash is not null;

        public bool HasStorage => _storageRoot is not null;

        public UInt256 Nonce => _nonce;
        public UInt256 Balance => _balance;
        public Hash256 StorageRoot => _storageRoot ?? Keccak.EmptyTreeHash;
        public Hash256 CodeHash => _codeHash ?? Keccak.OfAnEmptyString;
        public bool IsTotallyEmpty => _storageRoot is null && IsEmpty;
        public bool IsEmpty => _codeHash is null && _balance.IsZero && _nonce.IsZero;
        public bool IsContract => _codeHash is not null;

        public Account WithChangedBalance(in UInt256 newBalance)
        {
            return new(this, in _nonce, newBalance);
        }

        public Account WithChangedNonce(in UInt256 newNonce)
        {
            return new(this, in newNonce, in _balance);
        }

        public Account WithChangedStorageRoot(Hash256 newStorageRoot)
        {
            return new(this, newStorageRoot);
        }

        public Account WithChangedCodeHash(Hash256 newCodeHash)
        {
            return new(newCodeHash, this);
        }

        public AccountStruct ToStruct() => new(in _nonce, in _balance, StorageRoot, CodeHash);

        public bool Equals(Account? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return _nonce == other._nonce &&
                _balance == other._balance &&
                _codeHash == other._codeHash &&
                _storageRoot == other._storageRoot;
        }

        public override bool Equals(object? obj) => Equals(obj as Account);

        public override int GetHashCode() => throw new NotImplementedException();

        public static bool operator ==(Account? left, Account? right)
        {
            if (left is not null)
            {
                return left.Equals(right);
            }
            return right is null;
        }

        public static bool operator !=(Account? left, Account? right) => !(left == right);
    }

    public readonly struct AccountStruct
    {
        private static readonly AccountStruct _totallyEmpty = Account.TotallyEmpty.ToStruct();
        public static ref readonly AccountStruct TotallyEmpty => ref _totallyEmpty;

        private readonly UInt256 _balance;
        private readonly UInt256 _nonce = default;
        private readonly ValueHash256 _codeHash = Keccak.OfAnEmptyString.ValueHash256;
        private readonly ValueHash256 _storageRoot = Keccak.EmptyTreeHash.ValueHash256;

        public AccountStruct(in UInt256 nonce, in UInt256 balance, in ValueHash256 storageRoot, in ValueHash256 codeHash)
        {
            _balance = balance;
            _nonce = nonce;
            _codeHash = codeHash;
            _storageRoot = storageRoot;
        }

        public AccountStruct(in UInt256 nonce, in UInt256 balance)
        {
            _balance = balance;
            _nonce = nonce;
        }

        public AccountStruct(in UInt256 balance)
        {
            _balance = balance;
        }

        public bool HasCode => _codeHash != Keccak.OfAnEmptyString.ValueHash256;

        public bool HasStorage => _storageRoot != Keccak.EmptyTreeHash.ValueHash256;

        public UInt256 Nonce => _nonce;
        public UInt256 Balance => _balance;
        public ValueHash256 StorageRoot => _storageRoot;
        public ValueHash256 CodeHash => _codeHash;
        public bool IsTotallyEmpty => IsEmpty && _storageRoot == Keccak.EmptyTreeHash.ValueHash256;
        public bool IsEmpty => Balance.IsZero && Nonce.IsZero && _codeHash == Keccak.OfAnEmptyString.ValueHash256;
        public bool IsContract => _codeHash != Keccak.OfAnEmptyString.ValueHash256;
        public bool IsNull
        {
            get
            {
                // The following branchless code is generated by the JIT compiler for the IsNull property on x64
                //
                // Method Nethermind.Core.AccountStruct:get_IsNull():bool:this (FullOpts)
                // G_M000_IG01:
                //        vzeroupper
                //
                // G_M000_IG02:
                //        vmovups  ymm0, ymmword ptr [rcx]
                //        vpor     ymm0, ymm0, ymmword ptr [rcx+0x20]
                //        vpor     ymm0, ymm0, ymmword ptr [rcx+0x40]
                //        vpor     ymm0, ymm0, ymmword ptr [rcx+0x60]
                //        vptest   ymm0, ymm0
                //        sete     al
                //        movzx    rax, al
                //
                // G_M000_IG03:                ;; offset=0x0021
                //        vzeroupper
                //        ret
                // ; Total bytes of code: 37

                return (Unsafe.As<UInt256, Vector256<byte>>(ref Unsafe.AsRef(in _balance)) |
                    Unsafe.As<UInt256, Vector256<byte>>(ref Unsafe.AsRef(in _nonce)) |
                    Unsafe.As<ValueHash256, Vector256<byte>>(ref Unsafe.AsRef(in _codeHash)) |
                    Unsafe.As<ValueHash256, Vector256<byte>>(ref Unsafe.AsRef(in _storageRoot))) == default;
            }
        }
    }
}
