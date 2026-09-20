using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Contracts.Transport;

namespace Zapret2Pilot.Broker.Transport;

internal static class BrokerPipeFrameIO
{
    public static async Task<byte[]?> ReadPayloadAsync(
        Stream stream,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumPayloadBytes,
            BrokerProtocolLimits.MaxFrameBytes);

        byte[] header = new byte[BrokerProtocolLimits.LengthPrefixBytes];
        int headerBytes = await ReadExactlyOrEofAsync(
            stream,
            header,
            BrokerProtocolLimits.FrameHeaderTimeout,
            cancellationToken).ConfigureAwait(false);

        if (headerBytes == 0)
        {
            return null;
        }

        if (headerBytes != header.Length)
        {
            throw new EndOfStreamException(
                "Broker pipe disconnected while reading the frame header.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength <= 0)
        {
            throw new InvalidDataException(
                "Broker frame length must be positive.");
        }

        if (payloadLength > maximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"Broker frame length {payloadLength} exceeds the {maximumPayloadBytes}-byte limit.");
        }

        byte[] payload = new byte[payloadLength];
        int bodyBytes = await ReadExactlyOrEofAsync(
            stream,
            payload,
            BrokerProtocolLimits.FrameBodyTimeout,
            cancellationToken).ConfigureAwait(false);

        if (bodyBytes != payloadLength)
        {
            throw new EndOfStreamException(
                "Broker pipe disconnected while reading the frame body.");
        }

        return payload;
    }

    public static async Task WritePayloadAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] frame = BrokerFrameCodec.Encode(
            payload.Span,
            maximumPayloadBytes);

        await WriteBytesAsync(
            stream,
            frame,
            BrokerProtocolLimits.ResponseWriteTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    public static Task WriteFramedBytesAsync(
        Stream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
        => WriteBytesAsync(
            stream,
            frame,
            BrokerProtocolLimits.ResponseWriteTimeout,
            cancellationToken);

    private static async Task<int> ReadExactlyOrEofAsync(
        Stream stream,
        Memory<byte> buffer,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer[total..],
                deadline.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return total;
            }

            total += read;
        }

        return total;
    }

    private static async Task WriteBytesAsync(
        Stream stream,
        ReadOnlyMemory<byte> bytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        await stream.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
        await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
    }
}
