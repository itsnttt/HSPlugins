using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TheBirdOfHermes.Audio
{
    public class AudioData
    {
        /// <summary>
        /// Represents an array of sample data for audio processing. Each element in the array corresponds to an audio sample,
        /// with the number of samples depending on the audio's duration, sample rate, and number of channels.
        /// </summary>
        public float[] Samples;

        /// <summary>
        /// Represents the number of audio samples processed or played per second.
        /// Determines the precision and quality of the audio data.
        /// </summary>
        public int SampleRate;

        /// <summary>
        /// Specifies the number of audio channels present in the audio data. A value of 1 indicates mono audio, while 2 indicates stereo.
        /// This value is used in conjunction with the sample rate and duration to determine the structure and processing of the audio data.
        /// </summary>
        public int Channels;

        /// <summary>
        /// Represents the total duration of the audio data in seconds.
        /// Calculated based on the length of the sample array, sample rate, and number of channels.
        /// </summary>
        public float Duration => Samples.Length / (float)(SampleRate * Channels);

        private int offset;
        public byte[] EncodeWav()
        {
            offset = 0;
            int sampleCount = Samples.Length;
            int bytesPerSample = 2;
            int blockAlign = Channels * bytesPerSample;
            int byteRate = SampleRate * blockAlign;
            int dataSize = sampleCount * bytesPerSample;
            int fileSize = 44 + dataSize;

            byte[] wav = new byte[fileSize];

            wav[offset++] = (byte)'R';
            wav[offset++] = (byte)'I';
            wav[offset++] = (byte)'F';
            wav[offset++] = (byte)'F';
            WriteInt(wav, fileSize - 8);
            wav[offset++] = (byte)'W';
            wav[offset++] = (byte)'A';
            wav[offset++] = (byte)'V';
            wav[offset++] = (byte)'E';

            wav[offset++] = (byte)'f';
            wav[offset++] = (byte)'m';
            wav[offset++] = (byte)'t';
            wav[offset++] = (byte)' ';
            WriteInt(wav, 16);
            WriteShort(wav, 1);
            WriteShort(wav, (short)Channels);
            WriteInt(wav, SampleRate);
            WriteInt(wav, byteRate);
            WriteShort(wav, (short)blockAlign);
            WriteShort(wav, (short)(bytesPerSample * 8));

            wav[offset++] = (byte)'d';
            wav[offset++] = (byte)'a';
            wav[offset++] = (byte)'t';
            wav[offset++] = (byte)'a';
            WriteInt(wav, dataSize);

            for (int i = 0; i < sampleCount; i++)
            {
                short s = (short)(Mathf.Clamp(Samples[i], -1f, 1f) * 32767f);
                wav[offset++] = (byte)(s & 0xFF);
                wav[offset++] = (byte)((s >> 8) & 0xFF);
            }

            return wav;
        }

        private void WriteInt(byte[] buffer, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
            offset += 4;
        }

        private void WriteShort(byte[] buffer, short value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            offset += 2;
        }
    }
}