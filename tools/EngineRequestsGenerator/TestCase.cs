﻿// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace EngineRequestsGenerator;

public enum TestCase
{
    [TestCaseMetadata("Warmup", "warmup")]
    Warmup,

    [TestCaseMetadata("ETH transfers", "simple ETH transfers")]
    Transfers,

    [TestCaseMetadata("Tx with big zero data", "single transaction with large extra full of zeros")]
    TxDataZero,

    [TestCaseMetadata("Keccak256 from 1 byte", "keccak calculations based on 1-byte source data")]
    Keccak256From1Byte,

    [TestCaseMetadata("Keccak256 from 8 bytes", "keccak calculations based on 8-byte source data")]
    Keccak256From8Bytes,

    [TestCaseMetadata("Keccak256 from 32 bytes", "keccak calculations based on 32-byte source data")]
    Keccak256From32Bytes,

    [TestCaseMetadata("Push0", "endlessly pushing zeros to stack (1000 per 1 contract)")]
    Push0,

    [TestCaseMetadata("Push0-Pop", "endlessly pushing zeros to stack, then popping it")]
    Push0Pop,

    [TestCaseMetadata("Gas", "endlessly pushing amount of remaining gas to stack (1000 per 1 contract)")]
    Gas,

    [TestCaseMetadata("Gas-Pop", "endlessly pushing amount of remaining gas to stack, then popping it")]
    GasPop,

    [TestCaseMetadata("SelfBalance", "endlessly pushing self balance to stack (1000 per 1 contract)")]
    SelfBalance,

    [TestCaseMetadata("JumpDest", "block full of JumpDest opcode only")]
    JumpDest,

    [TestCaseMetadata("MSize", "endlessly pushing memory size to stack (1000 per 1 contract)")]
    MSize,

    [TestCaseMetadata("MStore - zero", "endlessly pushing zero value to memory with offset zero")]
    MStoreZero,

    [TestCaseMetadata("MStore - random", "endlessly pushing random value to memory with offset zero")]
    MStoreRandom,

    [TestCaseMetadata("Caller", "endlessly pushing caller address to stack (1000 per 1 contract)")]
    Caller,

    [TestCaseMetadata("Caller-Pop", "endlessly pushing caller address to stack, then popping it")]
    CallerPop,

    [TestCaseMetadata("Address", "endlessly pushing account address to stack (1000 per 1 contract)")]
    Address,

    [TestCaseMetadata("Origin", "endlessly pushing execution origination address to stack (1000 per 1 contract)")]
    Origin,

    [TestCaseMetadata("CoinBase", "endlessly pushing current block's coinbase to stack (1000 per 1 contract)")]
    CoinBase,

    [TestCaseMetadata("Timestamp", "endlessly pushing current block's timestamp to stack (1000 per 1 contract)")]
    Timestamp,

    [TestCaseMetadata("Number", "endlessly pushing current block's number to stack (1000 per 1 contract)")]
    Number,

    [TestCaseMetadata("PrevRandao", "endlessly pushing previous block's randao mix to stack (1000 per 1 contract)")]
    PrevRandao,

    [TestCaseMetadata("GasLimit", "endlessly pushing current block's gas limit to stack (1000 per 1 contract)")]
    GasLimit,

    [TestCaseMetadata("ChainId", "endlessly pushing chain ID to stack (1000 per 1 contract)")]
    ChainId,

    [TestCaseMetadata("BaseFee", "endlessly pushing current base fee to stack (1000 per 1 contract)")]
    BaseFee,

    [TestCaseMetadata("BlobBaseFee", "endlessly pushing current blob base fee to stack (1000 per 1 contract)")]
    BlobBaseFee,

    [TestCaseMetadata("BlobHash", "endlessly pushing zero as index and BlobHash opcode to stack when there were no blobs (1000 per 1 contract)")]
    BlobHashZero,

    [TestCaseMetadata("CodeCopy", "endlessly loading 32-bytes of code to the memory")]
    CodeCopy,

    // [TestCaseMetadata("BalanceNonExisting", "checking balances of non existing accounts")]
    // BalanceNonExisting,

    [TestCaseMetadata("EcRecover precompile", "EcRecover precompile calculations")]
    EcRecover,

    [TestCaseMetadata("SHA-2 precompile from 1 byte", "SHA-2 precompile calculations based on 1-byte source data")]
    SHA2From1Byte,

    [TestCaseMetadata("SHA-2 precompile from 8 bytes", "SHA-2 precompile calculations based on 8-byte source data")]
    SHA2From8Bytes,

    [TestCaseMetadata("SHA-2 precompile from 32 bytes", "SHA-2 precompile calculations based on 32-byte source data")]
    SHA2From32Bytes,

    [TestCaseMetadata("SHA-2 precompile from 128 bytes", "SHA-2 precompile calculations based on 128-byte source data")]
    SHA2From128Bytes,

    [TestCaseMetadata("Ripemd-160 precompile from 1 byte", "Ripemd-160 precompile calculations based on 1-byte source data")]
    RipemdFrom1Byte,

    [TestCaseMetadata("Ripemd-160 precompile from 8 bytes", "Ripemd-160 precompile calculations based on 8-byte source data")]
    RipemdFrom8Bytes,

    [TestCaseMetadata("Ripemd-160 precompile from 32 bytes", "Ripemd-160 precompile calculations based on 32-byte source data")]
    RipemdFrom32Bytes,

    [TestCaseMetadata("Ripemd-160 precompile from 128 bytes", "Ripemd-160 precompile calculations based on 128-byte source data")]
    RipemdFrom128Bytes,

    [TestCaseMetadata("Identity precompile from 1 byte", "Identity precompile call based on 1-byte source data")]
    IdentityFrom1Byte,

    [TestCaseMetadata("Identity precompile from 8 bytes", "Identity precompile call based on 8-byte source data")]
    IdentityFrom8Bytes,

    [TestCaseMetadata("Identity precompile from 32 bytes", "Identity precompile call based on 32-byte source data")]
    IdentityFrom32Bytes,

    [TestCaseMetadata("Identity precompile from 128 bytes", "Identity precompile call based on 128-byte source data")]
    IdentityFrom128Bytes,

    [TestCaseMetadata("Modexp min gas, base heavy", "Modexp precompile consuming 200 gas (minimum value), with base and modulo byte size equal 192 and exponent equal 3 (0b11 - 2x 1s in binary)")]
    ModexpMinGasBaseHeavy,

    [TestCaseMetadata("Modexp min gas, exp heavy", "Modexp precompile consuming 200 gas (minimum value), with base and modulo byte size equal 8 and exponent equal (603x 1s in binary)")]
    ModexpMinGasExpHeavy,

    [TestCaseMetadata("Modexp min gas, balanced", "Modexp precompile consuming 200 gas (minimum value), with base and modulo byte size equal 40 and exponent equal 2^25 - 1 (25x 1s in binary)")]
    ModexpMinGasBalanced,

    [TestCaseMetadata("Modexp 215 gas, exp heavy", "Modexp precompile consuming 215 gas, with base and modulo byte size equal 8 and exponent equal 2^648 - 1 (648x 1s in binary, which is max possible exponent value)")]
    Modexp215GasExpHeavy,

    // [TestCaseMetadata("Modexp 1k gas, base heavy", "Modexp precompile consuming 1000 gas, with base and modulo byte size equal 440 and exponent equal 3 (0b11 - 2x 1s in binary)")]
    // Modexp1KGasBaseHeavy,

    // [TestCaseMetadata("Modexp 1k gas, balanced", "Modexp precompile consuming 1000 gas, with base and modulo byte size equal 56 and exponent equal 2^62 - 1 (62x 1s in binary)")]
    // Modexp1KGasBalanced,
    //
    // [TestCaseMetadata("Modexp 10k gas, exp heavy", "Modexp precompile consuming 10_500 gas, with base and modulo byte size equal 56 and exponent equal 2^648 - 1 (648x 1s in binary, which is max possible exponent value)")]
    // Modexp10KGasExpHeavy,
    //
    // [TestCaseMetadata("Modexp 135k gas, balanced", "Modexp precompile consuming 135_000 gas, with base and modulo byte size equal 200 and exponent equal 2^648 - 1 (648x 1s in binary, which is max possible exponent value)")]
    // Modexp135KGasBalanced,

    [TestCaseMetadata("Blake2f 1 round", "Blake2f precompile with 1 round of computations")]
    Blake1Round,

    [TestCaseMetadata("Blake2f 1k rounds", "Blake2f precompile with 1000 rounds of computations")]
    Blake1KRounds,

    [TestCaseMetadata("Blake2f 1M rounds", "Blake2f precompile with 1_000_000 rounds of computations")]
    Blake1MRounds,

    [TestCaseMetadata("Blake2f 10M rounds", "Blake2f precompile with 10_000_000 rounds of computations")]
    Blake10MRounds,

    // [TestCaseMetadata("Point evaluation - zeros", "Point evaluation precompile with just zeros as an argument")]
    // PointEvaluationZeros,

    [TestCaseMetadata("Point evaluation - one data", "Point evaluation precompile repeating computations on the same data")]
    PointEvaluationOneData,

    [TestCaseMetadata("SStore - one storage key, repeating zero value", "SStore - repeating storing zero in single storage key of single account")]
    SStoreOneAccountOneKeyZeroValue,

    [TestCaseMetadata("SStore - one storage key, repeating constant value", "SStore - repeating storing the same 32-byte word in single storage key of single account")]
    SStoreOneAccountOneKeyConstantValue,

    [TestCaseMetadata("SStore - one storage key, repeating random values", "SStore - repeating storing random 32-byte values in single storage key of single account")]
    SStoreOneAccountOneKeyRandomValue,

    [TestCaseMetadata("SStore - one storage key, repeating two values, zero and non-zero", "SStore - repeating storing zero and then storing 32-byte word in single storage key of single account")]
    SStoreOneAccountOneKeyTwoValues,

    [TestCaseMetadata("SStore - many accounts, consecutive storage keys, random values", "SStore - storing random 32-byte values in consecutive storage keys of many accounts")]
    SStoreManyAccountsConsecutiveKeysRandomValue,

    [TestCaseMetadata("SStore - many accounts, random storage keys, random values", "SStore - storing random 32-byte values in random storage keys of many accounts")]
    SStoreManyAccountsRandomKeysRandomValue,

    [TestCaseMetadata("SStore - many accounts, consecutive storage keys, zero values", "SStore - storing zeros in consecutive storage keys of many accounts")]
    SStoreManyAccountsConsecutiveKeysZeroValue,

    [TestCaseMetadata("SStore - many accounts, random storage keys, zero values", "SStore - storing zeros in random storage keys of many accounts")]
    SStoreManyAccountsRandomKeysZeroValue,
}
