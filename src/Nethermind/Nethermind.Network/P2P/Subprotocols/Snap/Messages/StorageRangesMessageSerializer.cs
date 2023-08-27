// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using DotNetty.Buffers;
using Nethermind.Core.Buffers;
using Nethermind.Core.Crypto;
using Nethermind.Serialization.Rlp;
using Nethermind.State.Snap;

namespace Nethermind.Network.P2P.Subprotocols.Snap.Messages
{
    public class StorageRangesMessageSerializer : IZeroMessageSerializer<StorageRangeMessage>
    {
        public void Serialize(IByteBuffer byteBuffer, StorageRangeMessage message)
        {
            (int contentLength, int allSlotsLength, int[] accountSlotsLengths, int proofsLength) = CalculateLengths(message);

            byteBuffer.EnsureWritable(Rlp.LengthOfSequence(contentLength), true);
            NettyRlpStream stream = new(byteBuffer);

            stream.StartSequence(contentLength);

            stream.Encode(message.RequestId);

            if (message.Slots is null || message.Slots.Length == 0)
            {
                stream.EncodeNullObject();
            }
            else
            {
                stream.StartSequence(allSlotsLength);

                for (int i = 0; i < message.Slots.Length; i++)
                {
                    stream.StartSequence(accountSlotsLengths[i]);

                    PathWithStorageSlot[] accountSlots = message.Slots[i];

                    for (int j = 0; j < accountSlots.Length; j++)
                    {
                        var slot = accountSlots[j];

                        int itemLength = Rlp.LengthOf(slot.Path) + Rlp.LengthOf(slot.SlotRlpValue);

                        stream.StartSequence(itemLength);
                        stream.Encode(slot.Path);
                        stream.Encode(slot.SlotRlpValue);
                    }

                }
            }

            if (message.Proofs is null || message.Proofs.Length == 0)
            {
                stream.EncodeNullObject();
            }
            else
            {
                stream.StartSequence(proofsLength);
                for (int i = 0; i < message.Proofs.Length; i++)
                {
                    stream.Encode(message.Proofs[i]);
                }
            }
        }

        public StorageRangeMessage Deserialize(IByteBuffer byteBuffer)
        {
            StorageRangeMessage message = new();
            NettyBufferMemoryOwner memoryOwner = new(byteBuffer);
            Rlp.ValueDecoderContext ctx = new Rlp.ValueDecoderContext(memoryOwner.Memory, true);

            ctx.ReadSequenceLength();

            message.RequestId = ctx.DecodeLong();
            message.MemoryOwner = memoryOwner;

            message.Slots = ctx.DecodeArray(SlotsDecoder.Instance);
            message.Proofs = ctx.DecodeArray(ByteArrayDecoder.Instance);

            byteBuffer.SetReaderIndex(byteBuffer.ReaderIndex + ctx.Position);

            return message;
        }

        public class SlotsDecoder: IRlpValueDecoder<PathWithStorageSlot[]>
        {
            public static SlotsDecoder Instance = new();
            public int GetLength(PathWithStorageSlot[] item, RlpBehaviors rlpBehaviors)
            {
                throw new System.NotImplementedException("used for deserialize only");
            }

            public PathWithStorageSlot[] Decode(ref Rlp.ValueDecoderContext decoderContext, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
            {
                return decoderContext.DecodeArray(SlotDecoder.Instance);
            }
        }

        public class SlotDecoder: IRlpValueDecoder<PathWithStorageSlot>
        {
            public static SlotDecoder Instance = new();
            public int GetLength(PathWithStorageSlot item, RlpBehaviors rlpBehaviors)
            {
                throw new System.NotImplementedException("used for deserialize only");
            }

            public PathWithStorageSlot Decode(ref Rlp.ValueDecoderContext decoderContext, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
            {
                decoderContext.ReadSequenceLength();
                Keccak path = decoderContext.DecodeKeccak();
                Memory<byte> value = decoderContext.DecodeByteArrayMemory().Value;

                PathWithStorageSlot data = new(path, value);

                return data;
            }
        }

        public class ByteArrayDecoder: IRlpValueDecoder<byte[]>
        {
            public static ByteArrayDecoder Instance = new();
            public int GetLength(byte[] item, RlpBehaviors rlpBehaviors)
            {
                throw new System.NotImplementedException("used for deserialize only");
            }

            public byte[] Decode(ref Rlp.ValueDecoderContext decoderContext, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
            {
                return decoderContext.DecodeByteArray();
            }
        }

        private (int contentLength, int allSlotsLength, int[] accountSlotsLengths, int proofsLength) CalculateLengths(StorageRangeMessage message)
        {
            int contentLength = Rlp.LengthOf(message.RequestId);

            int allSlotsLength = 0;
            int[] accountSlotsLengths = new int[message.Slots.Length];

            if (message.Slots is null || message.Slots.Length == 0)
            {
                allSlotsLength = 1;
            }
            else
            {
                for (var i = 0; i < message.Slots.Length; i++)
                {
                    int accountSlotsLength = 0;

                    var accountSlots = message.Slots[i];
                    foreach (PathWithStorageSlot slot in accountSlots)
                    {
                        int slotLength = Rlp.LengthOf(slot.Path) + Rlp.LengthOf(slot.SlotRlpValue);
                        accountSlotsLength += Rlp.LengthOfSequence(slotLength);
                    }

                    accountSlotsLengths[i] = accountSlotsLength;
                    allSlotsLength += Rlp.LengthOfSequence(accountSlotsLength);
                }
            }

            contentLength += Rlp.LengthOfSequence(allSlotsLength);

            int proofsLength = 0;
            if (message.Proofs is null || message.Proofs.Length == 0)
            {
                proofsLength = 1;
                contentLength++;
            }
            else
            {
                for (int i = 0; i < message.Proofs.Length; i++)
                {
                    proofsLength += Rlp.LengthOf(message.Proofs[i]);
                }

                contentLength += Rlp.LengthOfSequence(proofsLength);
            }



            return (contentLength, allSlotsLength, accountSlotsLengths, proofsLength);
        }
    }
}
