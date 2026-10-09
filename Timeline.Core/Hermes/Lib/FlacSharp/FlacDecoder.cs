using System;
using System.IO;
using System.Security.Cryptography;

namespace SimpleFlac
{
    public class FlacDecoder : IDisposable
    {
        private readonly Options _options;
        private readonly BitReader _reader;
        private readonly IncrementalHash _outputHasher;

        private class IncrementalHash : IDisposable
        {
            private readonly MD5 _md5;
            private bool _finalized;

            public IncrementalHash()
            {
                _md5 = MD5.Create();
            }

            public void AppendData(byte[] data, int offset, int length)
            {
                if (_finalized)
                    throw new InvalidOperationException("Hash already finalized.");
                _md5.TransformBlock(data, offset, length, null, 0);
            }

            public void GetCurrentHash(byte[] destination)
            {
                if (!_finalized)
                {
                    _md5.TransformFinalBlock(new byte[0], 0, 0);
                    _finalized = true;
                }
                byte[] hashResult = _md5.Hash;
                if (hashResult == null || hashResult.Length > destination.Length)
                    throw new InvalidOperationException("Failed to compute hash or destination buffer is too small.");
                Array.Copy(hashResult, destination, hashResult.Length);
            }

            public void Dispose()
            {
                _md5.Clear();
            }
        }

        private byte[] _expectedOutputHash = new byte[16];

        public long? StreamSampleCount { get; private set; }
        public int SampleRate { get; private set; }
        public int ChannelCount { get; private set; }
        public int BitsPerSample { get; private set; }
        public int BytesPerSample { get; private set; }
        public int MaxSamplesPerFrame { get; private set; }

        public long[][] BufferSamples { get; private set; }
        public byte[] BufferBytes { get; private set; }
        public int BufferSampleCount { get; private set; }
        public int BufferByteCount { get; private set; }
        public long RunningSampleCount { get; private set; }

        public int BlockAlign
        {
            get { return BytesPerSample * ChannelCount; }
        }

        public FlacDecoder(Stream input, Options options)
        {
            _options = options ?? new Options();
            _reader = new BitReader(input);
            BufferSamples = new long[][] { new long[0] };
            BufferBytes = new byte[0];
            try
            {
                ValidateOptions();
                ReadMetadata();
            }
            catch
            {
                _reader.Dispose();
                throw;
            }
            _outputHasher = _options.ValidateOutputHash ? new IncrementalHash() : null;
        }

        public FlacDecoder(Stream input) : this(input, null) { }

        public FlacDecoder(string path, Options options)
            : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan), options)
        {
        }

        public FlacDecoder(string path) : this(path, null) { }

        public void Dispose()
        {
            _reader.Dispose();
            if (_options.ValidateOutputHash)
            {
                _outputHasher.Dispose();
            }
        }

        private void ValidateOptions()
        {
            if (_options.ValidateOutputHash && !_options.ConvertOutputToBytes)
                throw new ArgumentException("Output hash validation requires conversion to bytes.");
        }

        private void ReadMetadata()
        {
            if (_reader.Read(32) != 0x664C6143)
                throw new Exception("FLAC stream marker not found.");

            bool foundLastMetadataBlock;
            do
            {
                foundLastMetadataBlock = _reader.Read(1) != 0;
                int type = (int)_reader.Read(7);
                int length = (int)_reader.Read(24);
                if (type == 0)
                {
                    ReadStreaminfoBlock();
                }
                else
                {
                    for (int i = 0; i < length; i++)
                        _reader.Skip(8);
                }
            } while (!foundLastMetadataBlock);

            if (BufferSamples == null)
                throw new Exception("Stream info metadata block not found.");
        }

        private void ReadStreaminfoBlock()
        {
            _reader.Skip(16); // Minimum block size (samples)
            MaxSamplesPerFrame = (int)_reader.Read(16);
            _reader.Skip(24); // Minimum frame size (bytes)
            _reader.Skip(24); // Maximum frame size (bytes)
            SampleRate = (int)_reader.Read(20);
            ChannelCount = (int)_reader.Read(3) + 1;
            BitsPerSample = (int)_reader.Read(5) + 1;
            long streamSampleCount = (long)_reader.Read(36);
            for (int i = 0; i < 16; i++)
                _expectedOutputHash[i] = (byte)_reader.Read(8);

            StreamSampleCount = streamSampleCount != 0 ? streamSampleCount : (long?)null;
            BytesPerSample = (BitsPerSample + 7) / 8;
            BufferSamples = new long[ChannelCount][];
            for (int ch = 0; ch < ChannelCount; ch++)
                BufferSamples[ch] = new long[MaxSamplesPerFrame];
            if (_options.ConvertOutputToBytes)
                BufferBytes = new byte[MaxSamplesPerFrame * BlockAlign];
        }

        public bool DecodeFrame()
        {
            if (_reader.HasReachedEnd)
            {
                if (StreamSampleCount != null && RunningSampleCount != StreamSampleCount)
                    throw new Exception("Stream sample count is incorrect.");

                if (_options.ValidateOutputHash)
                {
                    byte[] actualHash = new byte[16];
                    _outputHasher.GetCurrentHash(actualHash);
                    if (!ByteArraysEqual(actualHash, _expectedOutputHash))
                        throw new Exception("Output hash is incorrect.");
                }

                return false;
            }

            if (_reader.Read(15) != 0x7FFC)
                throw new Exception("Invalid frame sync code.");

            _reader.Skip(1);
            int blockSizeCode = (int)_reader.Read(4);
            int sampleRateCode = (int)_reader.Read(4);
            int channelLayout = (int)_reader.Read(4);
            int bitDepthCode = (int)_reader.Read(3);
            _reader.Skip(1);

            int codedNumberLeadingOnes = LeadingZeroCount(~(_reader.Read(8) << 56));
            for (int i = 1; i < codedNumberLeadingOnes; i++)
                _reader.Skip(8);

            int frameSampleCount;
            if (blockSizeCode == 1)
                frameSampleCount = 192;
            else if (blockSizeCode >= 2 && blockSizeCode <= 5)
                frameSampleCount = 576 << (blockSizeCode - 2);
            else if (blockSizeCode == 6)
                frameSampleCount = (int)_reader.Read(8) + 1;
            else if (blockSizeCode == 7)
                frameSampleCount = (int)_reader.Read(16) + 1;
            else if (blockSizeCode >= 8 && blockSizeCode <= 15)
                frameSampleCount = 256 << (blockSizeCode - 8);
            else
                throw new Exception("Reserved block size.");

            int frameSampleRate;
            if (sampleRateCode == 0)
                frameSampleRate = SampleRate;
            else if (sampleRateCode >= 1 && sampleRateCode <= 11)
                frameSampleRate = SampleRateCodes[sampleRateCode];
            else if (sampleRateCode == 12)
                frameSampleRate = (int)_reader.Read(8) * 1000;
            else if (sampleRateCode == 13)
                frameSampleRate = (int)_reader.Read(16);
            else if (sampleRateCode == 14)
                frameSampleRate = (int)_reader.Read(16) * 10;
            else
                throw new Exception("Reserved sample rate.");

            int frameBitsPerSample;
            if (bitDepthCode == 0)
                frameBitsPerSample = BitsPerSample;
            else if (bitDepthCode >= 1 && bitDepthCode <= 2)
                frameBitsPerSample = 8 + ((bitDepthCode - 1) * 4);
            else if (bitDepthCode >= 4 && bitDepthCode <= 6)
                frameBitsPerSample = 16 + ((bitDepthCode - 4) * 4);
            else if (bitDepthCode == 7)
                frameBitsPerSample = 32;
            else
                throw new Exception("Reserved bit depth.");

            int frameChannelCount;
            if (channelLayout >= 0 && channelLayout <= 7)
                frameChannelCount = channelLayout + 1;
            else if (channelLayout >= 8 && channelLayout <= 10)
                frameChannelCount = 2;
            else
                throw new Exception("Reserved channel layout.");

            if (frameSampleCount > MaxSamplesPerFrame)
                throw new Exception("Frame sample count exceeds maximum.");

            if (frameSampleRate != SampleRate || frameBitsPerSample != BitsPerSample || frameChannelCount != ChannelCount)
                throw new NotSupportedException("Unsupported audio property change.");

            _reader.Skip(8); // Frame header CRC

            BufferSampleCount = frameSampleCount;
            RunningSampleCount += frameSampleCount;
            DecodeSubframes(_reader, BitsPerSample, channelLayout, BufferSamples, BufferSampleCount);
            _reader.AlignToByte();
            _reader.Skip(16); // Whole frame CRC

            if (_options.ConvertOutputToBytes)
            {
                ConvertOutputToBytes(BitsPerSample, ChannelCount, BufferSamples, BufferSampleCount, BufferBytes, _options.AllowNonstandardByteOutput);
                BufferByteCount = BufferSampleCount * BlockAlign;
            }

            if (_options.ValidateOutputHash)
            {
                _outputHasher.AppendData(BufferBytes, 0, BufferByteCount);
            }

            return true;
        }

        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static int LeadingZeroCount(ulong value)
        {
            if (value == 0) return 64;
            int count = 0;
            if ((value & 0xFFFFFFFF00000000UL) == 0) { count += 32; value <<= 32; }
            if ((value & 0xFFFF000000000000UL) == 0) { count += 16; value <<= 16; }
            if ((value & 0xFF00000000000000UL) == 0) { count +=  8; value <<=  8; }
            if ((value & 0xF000000000000000UL) == 0) { count +=  4; value <<=  4; }
            if ((value & 0xC000000000000000UL) == 0) { count +=  2; value <<=  2; }
            if ((value & 0x8000000000000000UL) == 0) { count +=  1; }
            return count;
        }

        private static void DecodeSubframes(BitReader reader, int bitsPerSample, int channelLayout, long[][] result, int blockSize)
        {
            if (channelLayout >= 0 && channelLayout <= 7)
            {
                for (int ch = 0; ch < result.Length; ch++)
                    DecodeSubframe(reader, bitsPerSample, result[ch], 0, blockSize);
            }
            else if (channelLayout >= 8 && channelLayout <= 10)
            {
                DecodeSubframe(reader, bitsPerSample + (channelLayout == 9 ? 1 : 0), result[0], 0, blockSize);
                DecodeSubframe(reader, bitsPerSample + (channelLayout == 9 ? 0 : 1), result[1], 0, blockSize);
                if (channelLayout == 8)
                {
                    for (int i = 0; i < blockSize; i++)
                        result[1][i] = result[0][i] - result[1][i];
                }
                else if (channelLayout == 9)
                {
                    for (int i = 0; i < blockSize; i++)
                        result[0][i] += result[1][i];
                }
                else // channelLayout == 10
                {
                    for (int i = 0; i < blockSize; i++)
                    {
                        long side = result[1][i];
                        long right = result[0][i] - (side >> 1);
                        result[1][i] = right;
                        result[0][i] = right + side;
                    }
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException("channelLayout");
            }
        }

        private static void DecodeSubframe(BitReader reader, int bitsPerSample, long[] result, int offset, int length)
        {
            if (reader.Read(1) != 0)
                throw new Exception("Invalid subframe padding.");

            int type = (int)reader.Read(6);
            int shift = (int)reader.Read(1);
            if (shift == 1)
            {
                while (reader.Read(1) == 0)
                    shift++;
            }
            bitsPerSample -= shift;

            if (type == 0)
            {
                long v = reader.ReadSigned(bitsPerSample);
                for (int i = 0; i < length; i++)
                    result[offset + i] = v;
            }
            else if (type == 1)
            {
                for (int i = 0; i < length; i++)
                    result[offset + i] = reader.ReadSigned(bitsPerSample);
            }
            else if (type >= 8 && type <= 12)
            {
                DecodeFixedPredictionSubframe(reader, type - 8, bitsPerSample, result, offset, length);
            }
            else if (type >= 32 && type <= 63)
            {
                DecodeLinearPredictiveCodingSubframe(reader, type - 31, bitsPerSample, result, offset, length);
            }
            else
            {
                throw new Exception("Reserved subframe type.");
            }

            if (shift != 0)
            {
                for (int i = 0; i < length; i++)
                    result[offset + i] <<= shift;
            }
        }

        private static void DecodeFixedPredictionSubframe(BitReader reader, int predOrder, int bitsPerSample, long[] result, int offset, int length)
        {
            for (int i = 0; i < predOrder; i++)
                result[offset + i] = reader.ReadSigned(bitsPerSample);
            DecodeResiduals(reader, predOrder, result, offset, length);
            if (predOrder != 0)
            {
                long[] coefs = FixedPredictionCoefficients[predOrder];
                RestoreLinearPrediction(result, offset, length, coefs, 0, coefs.Length, 0);
            }
        }

        private static void DecodeLinearPredictiveCodingSubframe(BitReader reader, int lpcOrder, int bitsPerSample, long[] result, int offset, int length)
        {
            for (int i = 0; i < lpcOrder; i++)
                result[offset + i] = reader.ReadSigned(bitsPerSample);
            int precision = (int)reader.Read(4) + 1;
            int shift = (int)reader.ReadSigned(5);
            long[] coefs = new long[lpcOrder];
            for (int i = coefs.Length - 1; i >= 0; i--)
                coefs[i] = reader.ReadSigned(precision);
            DecodeResiduals(reader, lpcOrder, result, offset, length);
            RestoreLinearPrediction(result, offset, length, coefs, 0, coefs.Length, shift);
        }

        private static void DecodeResiduals(BitReader reader, int warmup, long[] result, int offset, int length)
        {
            int method = (int)reader.Read(2);
            if (method >= 2)
                throw new Exception("Reserved residual coding method.");
            int paramBits = method == 0 ? 4 : 5;
            int escapeParam = method == 0 ? 15 : 31;

            int partitionOrder = (int)reader.Read(4);
            int numPartitions = 1 << partitionOrder;
            if (length % numPartitions != 0)
                throw new Exception("Block size not divisible by number of Rice partitions.");
            int partitionSize = length / numPartitions;

            for (int i = 0; i < numPartitions; i++)
            {
                int start = i * partitionSize + (i == 0 ? warmup : 0);
                int end = (i + 1) * partitionSize;

                int param = (int)reader.Read(paramBits);
                if (param != escapeParam)
                {
                    for (int j = start; j < end; j++)
                        result[offset + j] = DecodeRice(reader, param);
                }
                else
                {
                    int numBits = (int)reader.Read(5);
                    for (int j = start; j < end; j++)
                        result[offset + j] = numBits != 0 ? reader.ReadSigned(numBits) : 0;
                }
            }
        }

        private static void RestoreLinearPrediction(long[] result, int resultOffset, int resultLength, long[] coefs, int coefsOffset, int coefsLength, int shift)
        {
            for (int i = 0; i < resultLength - coefsLength; i++)
            {
                long sum = 0;
                for (int j = 0; j < coefsLength; j++)
                    sum += result[resultOffset + i + j] * coefs[coefsOffset + j];
                result[resultOffset + i + coefsLength] += sum >> shift;
            }
        }

        private static long DecodeRice(BitReader reader, int k)
        {
            ulong data = reader.RawBuffer;
            int leadingZeroCount = LeadingZeroCount(data);
            int quotientBitCount = leadingZeroCount + 1;
            int fullBitCount = quotientBitCount + k;
            if (fullBitCount > BitReader.BitsAvailableWorstCase)
                return DecodeRiceFallback(reader, k);
            ulong v = (ulong)leadingZeroCount << k;
            if (k != 0)
                v |= (data << quotientBitCount) >> (64 - k);
            reader.Skip(fullBitCount);
            return (int)(v >> 1) ^ -(int)(v & 1);
        }

        private static long DecodeRiceFallback(BitReader reader, int k)
        {
            int leadingZeroCount = 0;
            while (reader.Read(1) == 0)
                leadingZeroCount++;
            ulong v = (ulong)leadingZeroCount << k;
            if (k != 0)
                v |= reader.Read(k);
            return (int)(v >> 1) ^ -(int)(v & 1);
        }

        private static void ConvertOutputToBytes(int bitsPerSample, int channelCount, long[][] samples, int sampleCount, byte[] bytes, bool allowNonstandard)
        {
            if (!allowNonstandard && (bitsPerSample % 8 != 0 || bitsPerSample == 8))
                throw new NotSupportedException("Unsupported bit depth.");

            int bytesPerSample = (bitsPerSample + 7) / 8;
            int blockAlign = bytesPerSample * channelCount;
            for (int ch = 0; ch < channelCount; ch++)
            {
                long[] src = samples[ch];
                int byteOffset = ch * bytesPerSample;
                if (bytesPerSample == 1)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        bytes[byteOffset] = (byte)src[i];
                        byteOffset += blockAlign;
                    }
                }
                else if (bytesPerSample == 2)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        short s = (short)src[i];
                        bytes[byteOffset    ] = (byte)(s);
                        bytes[byteOffset + 1] = (byte)(s >> 8);
                        byteOffset += blockAlign;
                    }
                }
                else if (bytesPerSample == 3)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        long s = src[i];
                        bytes[byteOffset    ] = (byte)s;
                        bytes[byteOffset + 1] = (byte)(s >> 8);
                        bytes[byteOffset + 2] = (byte)(s >> 16);
                        byteOffset += blockAlign;
                    }
                }
                else if (bytesPerSample == 4)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int s = (int)src[i];
                        bytes[byteOffset    ] = (byte)s;
                        bytes[byteOffset + 1] = (byte)(s >> 8);
                        bytes[byteOffset + 2] = (byte)(s >> 16);
                        bytes[byteOffset + 3] = (byte)(s >> 24);
                        byteOffset += blockAlign;
                    }
                }
                else
                {
                    throw new NotSupportedException("Unsupported bit depth.");
                }
            }
        }

        private static readonly int[] SampleRateCodes = new int[]
        {
            0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000
        };

        private static readonly long[][] FixedPredictionCoefficients = new long[][]
        {
            new long[] { },
            new long[] { 1 },
            new long[] { -1, 2 },
            new long[] { 1, -3, 3 },
            new long[] { -1, 4, -6, 4 }
        };

        private class BitReader : IDisposable
        {
            public const int BitsAvailableWorstCase = 57;

            private readonly Stream _stream;
            private ulong _buffer;
            private int _bufferDeficitBits;
            private int _streamOverreadBytes;

            public BitReader(Stream stream)
            {
                _stream = stream;
                _bufferDeficitBits = 64;
                ReplenishBuffer();
            }

            public void Dispose()
            {
                _stream.Dispose();
            }

            public bool HasReachedEnd => _streamOverreadBytes >= 8;

            public ulong RawBuffer => _buffer;

            private void ReplenishBuffer()
            {
                while (_bufferDeficitBits >= 8)
                {
                    int b = _stream.ReadByte();
                    if (b == -1)
                    {
                        _streamOverreadBytes++;
                        if (HasReachedEnd)
                        {
                            if (_bufferDeficitBits == 8)
                                return;
                            throw new EndOfStreamException();
                        }
                    }
                    else
                    {
                        _buffer |= (ulong)b << (_bufferDeficitBits - 8);
                    }
                    _bufferDeficitBits -= 8;
                }
            }

            public void Skip(int numBits)
            {
                if (numBits < 1 || numBits > BitsAvailableWorstCase)
                    throw new ArgumentOutOfRangeException("numBits");
                _buffer <<= numBits;
                _bufferDeficitBits += numBits;
                ReplenishBuffer();
            }

            public ulong Read(int numBits)
            {
                ulong x = _buffer >> (64 - numBits);
                Skip(numBits);
                return x;
            }

            public long ReadSigned(int numBits)
            {
                ulong x = Read(numBits);
                int shift = 64 - numBits;
                return (long)(x << shift) >> shift;
            }

            public void AlignToByte()
            {
                if (_bufferDeficitBits != 0)
                    Skip(8 - _bufferDeficitBits);
            }
        }

        public class Options
        {
            public bool ConvertOutputToBytes { get; set; }
            public bool ValidateOutputHash { get; set; }
            public bool AllowNonstandardByteOutput { get; set; }

            public Options()
            {
                ConvertOutputToBytes = true;
                ValidateOutputHash = true;
                AllowNonstandardByteOutput = false;
            }
        }
    }
}