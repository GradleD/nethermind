// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only


using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;

namespace Nethermind.Core.ExecutionRequest;

public enum ExecutionRequestType : byte
{
    Deposit = 0,
    WithdrawalRequest = 1,
    ConsolidationRequest = 2
}

public class ExecutionRequest
{
    public byte RequestType { get; set; }
    public byte[]? RequestData { get; set; }

    public void FlatEncode(Span<byte> buffer)
    {
        if (buffer.Length < RequestData!.Length + 1)
            throw new ArgumentException("Buffer too small");

        buffer[0] = RequestType;
        RequestData.CopyTo(buffer.Slice(1));
    }

    public override string ToString() => ToString(string.Empty);

    public string ToString(string indentation) => @$"{indentation}{nameof(ExecutionRequest)}
            {{{nameof(RequestType)}: {RequestType},
            {nameof(RequestData)}: {RequestData!.ToHexString()}}}";
}

public static class ExecutionRequestExtensions
{
    public static int GetRequestsByteSize(this IEnumerable<ExecutionRequest> requests)
    {
        int size = 0;
        foreach (ExecutionRequest request in requests)
        {
            size += request.RequestData!.Length + 1;
        }
        return size;
    }

    public static void FlatEncode(this ExecutionRequest[] requests, Span<byte> buffer)
    {
        int currentPosition = 0;

        foreach (ExecutionRequest request in requests)
        {
            Span<byte> internalBuffer = new byte[request.RequestData!.Length + 1];
            request.FlatEncode(internalBuffer);

            // Ensure the buffer has enough space to accommodate the new data
            if (currentPosition + internalBuffer.Length > buffer.Length)
            {
                throw new InvalidOperationException("Buffer is not large enough to hold all data of requests");
            }

            // Copy the internalBuffer to the buffer at the current position
            internalBuffer.CopyTo(buffer.Slice(currentPosition, internalBuffer.Length));
            currentPosition += internalBuffer.Length;
        }
    }

    public static void FlatEncodeWithoutType(this ExecutionRequest[] requests, Span<byte> buffer)
    {
        int currentPosition = 0;

        foreach (ExecutionRequest request in requests)
        {
            // Ensure the buffer has enough space to accommodate the new data
            if (currentPosition + request.RequestData!.Length > buffer.Length)
            {
                throw new InvalidOperationException("Buffer is not large enough to hold all data of requests");
            }

            // Copy the RequestData to the buffer at the current position
            request.RequestData.CopyTo(buffer.Slice(currentPosition, request.RequestData.Length));
            currentPosition += request.RequestData.Length;
        }
    }

    public static Hash256 CalculateHash(this IEnumerable<ExecutionRequest> requests)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            Span<byte> concatenatedHashes = new byte[32 * requests.Count()];
            int currentPosition = 0;
            // Compute sha256 for each request and concatenate them
            foreach (ExecutionRequest request in requests)
            {
                Span<byte> requestBuffer = new byte[request.RequestData!.Length + 1];
                request.FlatEncode(requestBuffer);
                sha256.ComputeHash(requestBuffer.ToArray()).CopyTo(concatenatedHashes.Slice(currentPosition, 32));
                currentPosition += 32;
            }

            // Compute sha256 of the concatenated hashes
            return new Hash256(sha256.ComputeHash(concatenatedHashes.ToArray()));
        }
    }

    public static bool IsSortedByType(this ExecutionRequest[] requests)
    {
        for (int i = 1; i < requests.Length; i++)
        {
            if (requests[i - 1].RequestType > requests[i].RequestType)
            {
                return false;
            }
        }
        return true;
    }
}
