// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Globalization;
using Nethermind.Core.Extensions;

//namespace Nethermind.Serialization.Json
//{
//    
//    public class ULongConverter : JsonConverter<ulong>
//    {
//        private readonly NumberConversion _conversion;

//        public ULongConverter()
//            : this(NumberConversion.Hex)
//        {
//        }

//        public ULongConverter(NumberConversion conversion)
//        {
//            _conversion = conversion;
//        }

//        public override void WriteJson(JsonWriter writer, ulong value, JsonSerializer serializer)
//        {
//            switch (_conversion)
//            {
//                case NumberConversion.Hex:
//                    writer.WriteValue(value == 0UL ? "0x0" : value.ToHexString(true));
//                    break;
//                case NumberConversion.Decimal:
//                    writer.WriteValue(value == 0 ? "0" : value.ToString());
//                    break;
//                case NumberConversion.Raw:
//                    writer.WriteValue(value);
//                    break;
//                default:
//                    throw new NotSupportedException();
//            }
//        }

//        public override ulong ReadJson(JsonReader reader, Type objectType, ulong existingValue, bool hasExistingValue, JsonSerializer serializer)
//        {
//            return reader.Value is ulong || reader.Value is int
//                ? (ulong)reader.Value
//                : FromString(reader.Value?.ToString());
//        }

//        public static ulong FromString(string s)
//        {
//            if (s is null)
//            {
//                throw new JsonException("null cannot be assigned to long");
//            }

//            if (s == "0x0")
//            {
//                return 0UL;
//            }

//            if (s.StartsWith("0x0"))
//            {
//                return ulong.Parse(s.AsSpan(2), NumberStyles.AllowHexSpecifier);
//            }

//            if (s.StartsWith("0x"))
//            {
//                Span<char> withZero = new(new char[s.Length - 1]);
//                withZero[0] = '0';
//                s.AsSpan(2).CopyTo(withZero.Slice(1));
//                return ulong.Parse(withZero, NumberStyles.AllowHexSpecifier);
//            }

//            return ulong.Parse(s, NumberStyles.Integer);
//        }
//    }
//}


namespace Nethermind.Serialization.Json
{
    using System.Buffers;
    using System.Buffers.Binary;
    using System.Buffers.Text;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class ULongConverter : JsonConverter<ulong>
    {
        public static ulong FromString(ReadOnlySpan<byte> s)
        {
            if (s.Length == 0)
            {
                throw new JsonException("null cannot be assigned to long");
            }

            if (s.SequenceEqual("0x0"u8))
            {
                return 0uL;
            }

            ulong value;
            if (s.StartsWith("0x"u8))
            {
                s = s.Slice(2);
                if (Utf8Parser.TryParse(s, out value, out _, 'x'))
                {
                    return value;
                }
            }
            else if (Utf8Parser.TryParse(s, out value, out _))
            {
                return value;
            }

            throw new JsonException("hex to long");
        }

        public override ulong Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetUInt64();
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                if (!reader.HasValueSequence)
                {
                    return FromString(reader.ValueSpan);
                }
                else
                {
                    return FromString(reader.ValueSequence.ToArray());
                }
            }

            throw new JsonException();
        }

        [SkipLocalsInit]
        public override void Write(
            Utf8JsonWriter writer,
            ulong value,
            JsonSerializerOptions options)
        {
            if (value == 0)
            {
                writer.WriteRawValue("\"0x0\""u8, skipInputValidation: true);
            }
            else
            {
                Span<byte> bytes = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
                ByteArrayConverter.Convert(writer, bytes, skipLeadingZeros: true);
            }
        }
    }
}
