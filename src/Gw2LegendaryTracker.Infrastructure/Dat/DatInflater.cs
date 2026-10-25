using System.Buffers.Binary;

namespace Gw2LegendaryTracker.Infrastructure.Dat;

/// <summary>
/// Decompresses the compressed entries of a Gw2.dat. The scheme is a Huffman coder with back-references:
/// the stream is read as big-endian bits out of little-endian 32-bit words, and the last word of every 64 KiB
/// block is a checksum that is skipped. The output size is stored in the header, so a result is only returned
/// when exactly that many bytes were produced.
/// </summary>
public static class DatInflater
{
    /// <summary>Words per 64 KiB block; the last word of each block is a checksum, not data.</summary>
    internal const int BlockWords = 0x4000;

    private const int MaxSymbols = 285;
    private const int MaxCodeBits = 32;

    // Code length of each byte value in the fixed tree that spells out the code lengths of the real trees.
    // Symbols of one length take consecutive codes in ascending symbol order, as in every canonical code.
    internal static readonly byte[] DictionaryLengths =
    [
        4, 10, 10, 7, 6, 6, 5, 4, 3, 3, 3, 4, 4, 7, 11, 15,
        15, 15, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        6, 16, 13, 9, 8, 7, 7, 7, 6, 5, 5, 6, 6, 10, 13, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        6, 16, 16, 10, 10, 10, 9, 8, 7, 7, 6, 8, 8, 11, 14, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        9, 16, 16, 9, 11, 10, 10, 9, 9, 8, 8, 11, 11, 14, 16, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        10, 16, 16, 12, 11, 11, 12, 10, 9, 9, 10, 11, 13, 15, 16, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        9, 16, 16, 16, 11, 11, 12, 12, 10, 10, 11, 15, 15, 16, 16, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        10, 16, 16, 16, 13, 16, 14, 12, 11, 10, 12, 16, 15, 16, 16, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        5, 16, 16, 16, 13, 11, 13, 12, 9, 10, 15, 16, 14, 16, 16, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
    ];

    private static readonly CanonicalCode Dictionary = CanonicalCode.FromLengths(DictionaryLengths);

    /// <summary>The size of the file once decompressed, as announced in the header of the stored data.</summary>
    public static uint InflatedSize(ReadOnlySpan<byte> stored)
    {
        if (stored.Length < 8)
        {
            throw new DatFormatException("De gecomprimeerde gegevens zijn te kort voor een header.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(stored[4..]);
    }

    /// <summary>Decompresses a whole entry, or at most <paramref name="limit"/> bytes from its start.</summary>
    public static byte[] Inflate(byte[] stored, uint? limit = null)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var bits = new BitReader(stored, BlockWords);
        bits.Take(32);
        var size = bits.Take(32);
        if (limit is { } max)
        {
            size = Math.Min(size, max);
        }

        var output = new byte[size];
        Fill(bits, output);
        return output;
    }

    private static void Fill(BitReader bits, byte[] output)
    {
        bits.Take(4);
        var copyBias = (int)bits.Take(4) + 1;
        var written = 0;

        while (written < output.Length)
        {
            var symbols = ReadCode(bits);
            var distances = ReadCode(bits);
            var blockCodes = (bits.Take(4) + 1) << 12;

            for (uint codes = 0; codes < blockCodes && written < output.Length; codes++)
            {
                var symbol = symbols.Read(bits);
                if (symbol < 0x100)
                {
                    output[written++] = (byte)symbol;
                    continue;
                }

                var copy = CopyLength(bits, symbol - 0x100) + copyBias;
                var distance = Distance(bits, distances.Read(bits)) + 1;
                if (distance > written)
                {
                    throw new DatFormatException("Een verwijzing wijst naar gegevens vóór het begin van het bestand.");
                }

                copy = Math.Min(copy, output.Length - written);
                for (var i = 0; i < copy; i++)
                {
                    output[written] = output[written - distance];
                    written++;
                }
            }
        }
    }

    private static int CopyLength(BitReader bits, int symbol)
    {
        var group = symbol / 4;
        var step = symbol % 4;
        if (group == 0)
        {
            return symbol;
        }

        if (group <= 6)
        {
            return ((1 << (group - 1)) * (4 + step)) | (group > 1 ? (int)bits.Take(group - 1) : 0);
        }

        return symbol == 28 ? 0xFF : throw new DatFormatException("Ongeldige lengtecode in de gecomprimeerde gegevens.");
    }

    private static int Distance(BitReader bits, int symbol)
    {
        var group = symbol / 2;
        var step = symbol % 2;
        if (group == 0)
        {
            return symbol;
        }

        if (group > 16)
        {
            throw new DatFormatException("Ongeldige afstandscode in de gecomprimeerde gegevens.");
        }

        return ((1 << (group - 1)) * (2 + step)) | (group > 1 ? (int)bits.Take(group - 1) : 0);
    }

    /// <summary>Reads the code lengths of one tree: runs of equal lengths, spelled out with the fixed dictionary tree.</summary>
    private static CanonicalCode ReadCode(BitReader bits)
    {
        var symbolCount = (int)bits.Take(16);
        if (symbolCount > MaxSymbols)
        {
            throw new DatFormatException("Te veel symbolen in de gecomprimeerde gegevens.");
        }

        var lengths = new byte[symbolCount];
        var remaining = symbolCount - 1;
        while (remaining >= 0)
        {
            var token = Dictionary.Read(bits);
            var length = (byte)(token & 0x1F);
            var run = (token >> 5) + 1;
            if (length == 0)
            {
                remaining -= run;
                continue;
            }

            for (; run > 0; run--, remaining--)
            {
                if (remaining < 0)
                {
                    throw new DatFormatException("De lengtes van de codes lopen over de rand van de tabel.");
                }

                lengths[remaining] = length;
            }
        }

        // A tree with no codes at all stands for one symbol that costs no bits.
        return lengths.Any(l => l != 0) ? CanonicalCode.FromLengths(lengths) : CanonicalCode.Single(symbolCount - 1);
    }

    /// <summary>MSB-first bit reader over little-endian 32-bit words that hides the checksum word of every block.</summary>
    internal sealed class BitReader
    {
        private readonly byte[] _data;
        private readonly int _blockWords;
        private readonly int _wordCount;
        private int _word;
        private ulong _window;
        private int _bits;

        public BitReader(byte[] data, int blockWords)
        {
            _data = data;
            _blockWords = blockWords;
            _wordCount = data.Length / 4;
            Refill();
        }

        /// <summary>The next <paramref name="count"/> bits (1 to 32) without consuming them; bits past the end read as zero.</summary>
        public uint Peek(int count) => (uint)(_window >> (64 - count));

        public void Consume(int count)
        {
            if (count > _bits)
            {
                throw new DatTruncatedException();
            }

            _window <<= count;
            _bits -= count;
            if (_bits <= 32)
            {
                Refill();
            }
        }

        public uint Take(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            var value = Peek(count);
            Consume(count);
            return value;
        }

        private void Refill()
        {
            while (_bits <= 32)
            {
                if (_blockWords != 0 && (_word + 1) % _blockWords == 0)
                {
                    _word++;
                }

                if (_word >= _wordCount)
                {
                    return;
                }

                var word = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_word * 4));
                _word++;
                _window |= (ulong)word << (32 - _bits);
                _bits += 32;
            }
        }
    }

    /// <summary>
    /// A canonical Huffman code in which the first code of a length is the numerically largest: the bits of every
    /// code are the complement of the textbook canonical code, so decoding works on the inverted stream.
    /// </summary>
    internal sealed class CanonicalCode
    {
        private readonly int _single;
        private readonly uint[] _first = new uint[MaxCodeBits + 1];
        private readonly int[] _count = new int[MaxCodeBits + 1];
        private readonly int[] _offset = new int[MaxCodeBits + 1];
        private readonly int[] _sorted;

        private CanonicalCode(int single, int symbols)
        {
            _single = single;
            _sorted = new int[symbols];
        }

        public static CanonicalCode Single(int symbol) => new(symbol, 0);

        public static CanonicalCode FromLengths(byte[] lengths)
        {
            var code = new CanonicalCode(-1, lengths.Length);
            foreach (var length in lengths)
            {
                if (length >= MaxCodeBits)
                {
                    throw new DatFormatException("Een code is te lang.");
                }

                if (length != 0)
                {
                    code._count[length]++;
                }
            }

            uint first = 0;
            var offset = 0;
            for (var length = 1; length < MaxCodeBits; length++)
            {
                first = (first + (uint)code._count[length - 1]) << 1;
                code._first[length] = first;
                code._offset[length] = offset;
                if ((ulong)first + (ulong)code._count[length] > 1UL << length)
                {
                    throw new DatFormatException("De codelengtes passen niet in een geldige boom.");
                }

                offset += code._count[length];
            }

            var next = (int[])code._offset.Clone();
            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                if (lengths[symbol] != 0)
                {
                    code._sorted[next[lengths[symbol]]++] = symbol;
                }
            }

            return code;
        }

        public int Read(BitReader bits)
        {
            if (_single >= 0)
            {
                return _single;
            }

            var inverted = ~bits.Peek(32);
            for (var length = 1; length < MaxCodeBits; length++)
            {
                var count = _count[length];
                if (count == 0)
                {
                    continue;
                }

                var value = inverted >> (32 - length);
                if (value >= _first[length] && value - _first[length] < (uint)count)
                {
                    bits.Consume(length);
                    return _sorted[_offset[length] + (int)(value - _first[length])];
                }
            }

            throw new DatFormatException("Ongeldige code in de gecomprimeerde gegevens.");
        }
    }
}
