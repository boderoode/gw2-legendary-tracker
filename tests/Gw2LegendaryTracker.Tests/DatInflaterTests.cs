using System.Buffers.Binary;
using Gw2LegendaryTracker.Infrastructure.Dat;

namespace Gw2LegendaryTracker.Tests;

/// <summary>
/// A small encoder for the archive's compression scheme, used only to test the decoder. It writes literals and
/// back-references with fixed-length codes, which is enough to cover bit order, checksum words, tree reading,
/// repeated sections and overlapping copies. It cannot show that the real archives decode; the spike tool does.
/// </summary>
internal sealed class TestEncoder
{
    private const int SectionCodes = 4096;
    private const int LiteralCodeBits = 9;
    private const int DistanceCodeBits = 6;
    private const int SymbolCount = 285;
    private const int DistanceSymbols = 34;

    private readonly List<uint> _words = [];
    private ulong _window;
    private int _bits;

    public static byte[] Encode(byte[] data, IReadOnlyList<(int Distance, int Length)>? copies = null, int bias = 1, uint? declaredSize = null, bool allowInvalidCopies = false)
    {
        var encoder = new TestEncoder();
        encoder.WriteBits(0, 32);
        encoder.WriteBits(declaredSize ?? (uint)data.Length, 32);
        encoder.WriteBits(0, 4);
        encoder.WriteBits((uint)(bias - 1), 4);

        var plan = new List<(bool IsCopy, int A, int B)>();
        var copyQueue = new Queue<(int Distance, int Length)>(copies ?? []);
        var position = 0;
        while (position < data.Length)
        {
            if (copyQueue.Count > 0 && (allowInvalidCopies || copyQueue.Peek().Distance <= position) && position + copyQueue.Peek().Length <= data.Length)
            {
                var (distance, length) = copyQueue.Dequeue();
                plan.Add((true, distance, length));
                position += length;
            }
            else
            {
                plan.Add((false, data[position], 0));
                position++;
            }
        }

        for (var at = 0; at < plan.Count; at += SectionCodes)
        {
            encoder.WriteLengths(SymbolCount, LiteralCodeBits);
            encoder.WriteLengths(DistanceSymbols, DistanceCodeBits);
            encoder.WriteBits(0, 4); // (0 + 1) << 12 = 4096 codes in this section
            foreach (var (isCopy, a, b) in plan.Skip(at).Take(SectionCodes))
            {
                if (!isCopy)
                {
                    encoder.WriteBits(~(uint)a & ((1u << LiteralCodeBits) - 1), LiteralCodeBits);
                    continue;
                }

                var (lengthSymbol, lengthExtra, lengthExtraBits) = LengthCode(b - bias);
                encoder.WriteBits(~(uint)(256 + lengthSymbol) & ((1u << LiteralCodeBits) - 1), LiteralCodeBits);
                encoder.WriteBits((uint)lengthExtra, lengthExtraBits);
                var (distanceSymbol, distanceExtra, distanceExtraBits) = DistanceCode(a - 1);
                encoder.WriteBits(~(uint)distanceSymbol & ((1u << DistanceCodeBits) - 1), DistanceCodeBits);
                encoder.WriteBits((uint)distanceExtra, distanceExtraBits);
            }
        }

        return encoder.Finish();
    }

    /// <summary>A stream whose trees each hold one symbol, so every code costs zero bits.</summary>
    public static byte[] EncodeRepeatedSymbol(byte value, int size)
    {
        var encoder = new TestEncoder();
        encoder.WriteBits(0, 32);
        encoder.WriteBits((uint)size, 32);
        encoder.WriteBits(0, 4);
        encoder.WriteBits(0, 4);
        encoder.WriteSingleSymbolTree(value + 1);
        encoder.WriteSingleSymbolTree(1);
        encoder.WriteBits(0, 4);
        return encoder.Finish();
    }

    public static (int Symbol, int Extra, int ExtraBits) LengthCode(int value)
    {
        if (value < 4)
        {
            return (value, 0, 0);
        }

        for (var symbol = 4; symbol < 28; symbol++)
        {
            var group = symbol / 4;
            var start = (1 << (group - 1)) * (4 + (symbol % 4));
            if (value >= start && value < start + (1 << (group - 1)))
            {
                return (symbol, value - start, group - 1);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value));
    }

    public static (int Symbol, int Extra, int ExtraBits) DistanceCode(int value)
    {
        if (value < 2)
        {
            return (value, 0, 0);
        }

        for (var symbol = 2; symbol < 34; symbol++)
        {
            var group = symbol / 2;
            var start = (1 << (group - 1)) * (2 + (symbol % 2));
            if (value >= start && value < start + (1 << (group - 1)))
            {
                return (symbol, value - start, group - 1);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value));
    }

    public void WriteBits(uint value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            _window = (_window << 1) | ((value >> i) & 1);
            _bits++;
            if (_bits == 32)
            {
                AddWord((uint)_window);
                _window = 0;
                _bits = 0;
            }
        }
    }

    public byte[] Finish()
    {
        if (_bits > 0)
        {
            AddWord((uint)(_window << (32 - _bits)));
        }

        var bytes = new byte[_words.Count * 4];
        for (var i = 0; i < _words.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), _words[i]);
        }

        return bytes;
    }

    private void AddWord(uint word)
    {
        // The last word of every 64 KiB block is a checksum that the decoder must skip.
        if ((_words.Count + 1) % DatInflater.BlockWords == 0)
        {
            _words.Add(0xDEADBEEF);
        }

        _words.Add(word);
    }

    /// <summary>All symbols get the same code length; the lengths are spelled out from the highest symbol down.</summary>
    private void WriteLengths(int symbolCount, int length)
    {
        WriteBits((uint)symbolCount, 16);
        for (var i = 0; i < symbolCount; i++)
        {
            WriteDictionaryToken(length);
        }
    }

    private void WriteSingleSymbolTree(int symbolCount)
    {
        WriteBits((uint)symbolCount, 16);
        var remaining = symbolCount;
        while (remaining > 0)
        {
            var run = Math.Min(8, remaining);
            WriteDictionaryToken(((run - 1) << 5) | 0);
            remaining -= run;
        }
    }

    private void WriteDictionaryToken(int token)
    {
        var lengths = DatInflater.DictionaryLengths;
        var length = lengths[token];
        uint first = 0;
        for (var l = 1; l < length; l++)
        {
            first = (first + (uint)lengths.Count(x => x == l)) << 1;
        }

        var rank = lengths.Take(token).Count(x => x == length);
        var canonical = first + (uint)rank;
        WriteBits(~canonical & ((1u << length) - 1), length);
    }
}

public class DatInflaterTests
{
    private static byte[] Random(int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Fact]
    public void The_fixed_dictionary_is_a_complete_code()
    {
        var kraft = DatInflater.DictionaryLengths.Sum(l => Math.Pow(2, -l));

        Assert.Equal(256, DatInflater.DictionaryLengths.Length);
        Assert.Equal(1.0, kraft, 12);
    }

    [Fact]
    public void Literals_round_trip()
    {
        var data = Random(1000, 1);

        Assert.Equal(data, DatInflater.Inflate(TestEncoder.Encode(data)));
    }

    [Fact]
    public void An_empty_file_gives_no_bytes()
    {
        Assert.Empty(DatInflater.Inflate(TestEncoder.Encode([])));
    }

    [Fact]
    public void The_announced_size_is_read_from_the_header()
    {
        var stored = TestEncoder.Encode(Random(123, 2));

        Assert.Equal(123u, DatInflater.InflatedSize(stored));
        Assert.Throws<DatFormatException>(() => DatInflater.InflatedSize(new byte[7]));
    }

    [Fact]
    public void Data_that_spans_several_sections_and_a_checksum_word_round_trips()
    {
        // 80,000 literals of 9 bits is about 22,000 words: past the first 64 KiB block and over 19 sections.
        var data = Random(80_000, 3);

        Assert.Equal(data, DatInflater.Inflate(TestEncoder.Encode(data)));
    }

    [Fact]
    public void Back_references_copy_earlier_output_including_overlapping_runs()
    {
        var data = new byte[200];
        for (var i = 0; i < 10; i++)
        {
            data[i] = (byte)(i + 1);
        }

        // 10 literals, then: copy 5 from 10 back, copy 90 from distance 1 (a run), copy 60 from distance 7.
        for (var i = 10; i < 15; i++)
        {
            data[i] = data[i - 10];
        }

        for (var i = 15; i < 105; i++)
        {
            data[i] = data[i - 1];
        }

        for (var i = 105; i < 165; i++)
        {
            data[i] = data[i - 7];
        }

        for (var i = 165; i < 200; i++)
        {
            data[i] = (byte)(i * 3);
        }

        var stored = TestEncoder.Encode(data, [(10, 5), (1, 90), (7, 60)]);

        Assert.Equal(data, DatInflater.Inflate(stored));
        Assert.True(stored.Length < 200 * 9 / 8 + 40, "copies should make the stream smaller than literals only");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public void The_copy_length_bias_is_applied(int bias)
    {
        var data = new byte[60];
        for (var i = 0; i < 4; i++)
        {
            data[i] = (byte)(10 + i);
        }

        for (var i = 4; i < 60; i++)
        {
            data[i] = data[i - 4];
        }

        Assert.Equal(data, DatInflater.Inflate(TestEncoder.Encode(data, [(4, 56)], bias)));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(5, 5)]
    [InlineData(100, 300)]
    public void Length_and_distance_codes_cover_their_ranges(int value, int distanceValue)
    {
        var (symbol, extra, bits) = TestEncoder.LengthCode(value);
        Assert.InRange(symbol, 0, 27);
        Assert.True(extra < (1 << Math.Max(bits, 1)));

        var (distanceSymbol, distanceExtra, distanceBits) = TestEncoder.DistanceCode(distanceValue);
        Assert.InRange(distanceSymbol, 0, 33);
        Assert.True(distanceExtra < (1 << Math.Max(distanceBits, 1)));
    }

    [Fact]
    public void A_tree_with_one_symbol_costs_no_bits()
    {
        var output = DatInflater.Inflate(TestEncoder.EncodeRepeatedSymbol((byte)'A', 300));

        Assert.Equal(300, output.Length);
        Assert.All(output, b => Assert.Equal((byte)'A', b));
    }

    [Fact]
    public void A_limit_returns_only_the_start_of_the_file()
    {
        var data = Random(5000, 4);

        var prefix = DatInflater.Inflate(TestEncoder.Encode(data), limit: 100);

        Assert.Equal(data[..100], prefix);
    }

    [Fact]
    public void A_limit_larger_than_the_file_returns_the_whole_file()
    {
        var data = Random(50, 5);

        Assert.Equal(data, DatInflater.Inflate(TestEncoder.Encode(data), limit: 10_000));
    }

    [Fact]
    public void Cut_off_input_is_reported_as_truncated()
    {
        var stored = TestEncoder.Encode(Random(2000, 6));

        Assert.Throws<DatTruncatedException>(() => DatInflater.Inflate(stored[..(stored.Length / 2)]));
        Assert.Throws<DatTruncatedException>(() => DatInflater.Inflate([]));
    }

    [Fact]
    public void A_reference_before_the_start_of_the_output_is_refused()
    {
        var stored = TestEncoder.Encode(new byte[10], [(3, 4)], allowInvalidCopies: true);

        var ex = Assert.Throws<DatFormatException>(() => DatInflater.Inflate(stored));

        Assert.Contains("vóór het begin", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Random_noise_never_crashes_the_decoder_with_anything_but_a_format_error()
    {
        var rng = new Random(7);
        for (var i = 0; i < 300; i++)
        {
            var noise = new byte[rng.Next(8, 400)];
            rng.NextBytes(noise);
            BinaryPrimitives.WriteUInt32LittleEndian(noise.AsSpan(4), (uint)rng.Next(0, 5000));

            try
            {
                DatInflater.Inflate(noise);
            }
            catch (DatFormatException)
            {
            }
        }
    }

    [Fact]
    public void Corrupted_streams_fail_with_a_format_error_or_decode_to_the_declared_size()
    {
        var data = Random(3000, 8);
        var stored = TestEncoder.Encode(data, [(2, 40)]);
        var rng = new Random(9);
        for (var i = 0; i < 200; i++)
        {
            var copy = (byte[])stored.Clone();
            copy[rng.Next(8, copy.Length)] ^= (byte)rng.Next(1, 256);
            try
            {
                Assert.Equal(3000, DatInflater.Inflate(copy).Length);
            }
            catch (DatFormatException)
            {
            }
        }
    }

    [Fact]
    public void An_archive_returns_decompressed_content_and_strips_block_checksums_of_plain_entries()
    {
        var content = Random(70_000, 10);
        var compressed = TestEncoder.Encode(content);

        // A plain entry: 65,532 bytes + 4 checksum bytes, then 4,468 bytes + 4 checksum bytes.
        var plain = new byte[70_000 + 8];
        content.AsSpan(0, 65_532).CopyTo(plain);
        content.AsSpan(65_532).CopyTo(plain.AsSpan(65_536));

        using var dat = new SyntheticDat(
            [],
            files: [(7, plain, false), (8, compressed, true)]);
        using var archive = DatArchive.Open(dat.Path);

        Assert.Equal(content, archive.Read(8));
        Assert.Equal(content, archive.Read(7));
        Assert.Equal(content[..10], archive.ReadPrefix(7, 10));
        Assert.Equal(content[..70_000], archive.ReadPrefix(7, 100_000));
        Assert.Equal(content[..500], archive.ReadPrefix(8, 500));
        Assert.Equal(content, archive.ReadPrefix(8, 100_000));
    }

    [Fact]
    public void A_prefix_of_a_large_compressed_file_grows_its_read_until_the_tables_fit()
    {
        var content = Random(20_000, 11);
        using var dat = new SyntheticDat([], files: [(8, TestEncoder.Encode(content), true)]);
        using var archive = DatArchive.Open(dat.Path);

        Assert.Equal(content[..16], archive.ReadPrefix(8, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => archive.ReadPrefix(8, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => archive.ReadPrefix(500, 1));
    }
}
