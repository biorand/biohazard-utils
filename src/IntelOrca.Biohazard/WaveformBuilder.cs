﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using NVorbis;

namespace IntelOrca.Biohazard
{
    public class WaveformBuilder
    {
        private const uint g_riffMagic = 0x46464952;
        private const uint g_waveMagic = 0x45564157;
        private const uint g_fmtMagic = 0x20746D66;
        private const uint g_dataMagic = 0x61746164;

        private MemoryStream _stream = new MemoryStream();
        private WaveHeader _header;
        private bool _headerWritten;
        private bool _finished;
        private double _initialSilence;

        public int Channels { get; private set; }
        public int SampleRate { get; private set; }
        public float Volume { get; private set; } = 1;
        public double Duration => (double)_stream.Length / _header.nAvgBytesPerSec;

        public static bool IsSupportedExtension(string path)
        {
            return path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }

        public WaveformBuilder(int channels = 0, int sampleRate = 0, float volume = 1)
        {
            Channels = channels;
            SampleRate = sampleRate;
            Volume = volume;
        }

        private void WriteHeader(in WaveHeader header)
        {
            if (_headerWritten)
                throw new InvalidOperationException();

            var bw = new BinaryWriter(_stream);
            _header = header;
            if (Channels != 0)
            {
                _header.nChannels = (ushort)Channels;
            }
            if (SampleRate != 0)
            {
                _header.nSamplesPerSec = (uint)SampleRate;
            }
            if (Channels != 0 || SampleRate != 0)
            {
                _header.nBlockAlign = (ushort)((_header.nChannels * _header.wBitsPerSample) / 8);
                _header.nAvgBytesPerSec = _header.nBlockAlign * _header.nSamplesPerSec;
            }
            bw.Write(_header);
            _headerWritten = true;

            AppendSilence(_initialSilence);
        }

        private void Finish()
        {
            var stream = _stream;

            // Clamp or expand the stream to the correct length (if waveform is not expected length)
            var correctLength = ((uint)(stream.Length - 44) / _header.nBlockAlign) * _header.nBlockAlign;
            stream.SetLength(44 + correctLength);

            // Write RIFF length
            var bw = new BinaryWriter(stream);
            stream.Position = 4;
            bw.Write((uint)(stream.Length - 8));

            // WRITE data length
            stream.Position = 40;
            bw.Write(correctLength);

            stream.Position = stream.Length;
            _finished = true;
        }

        public unsafe byte[] GetPCM()
        {
            var ms = new MemoryStream();
            _stream.Position = sizeof(WaveHeader);
            _stream.CopyTo(ms);
            _stream.Position = _stream.Length;
            return ms.ToArray();
        }

        public byte[] ToArray(ulong? sapHeader = null)
        {
            using var ms = new MemoryStream();
            Save(ms, sapHeader);
            return ms.ToArray();
        }

        public void Save(string path, ulong sapHeader = 1)
        {
            using var fs = new FileStream(path, FileMode.Create);
            var sapHeaderReal = path.EndsWith(".sap", StringComparison.OrdinalIgnoreCase) ? sapHeader : (ulong?)null;
            Save(fs, sapHeaderReal);
        }

        public void Save(Stream stream, ulong? sapHeader = 1)
        {
            if (!_headerWritten)
                throw new InvalidOperationException();

            if (!_finished)
                Finish();

            if (sapHeader is ulong h)
            {
                var bw = new BinaryWriter(stream);
                bw.Write(h);
            }
            _stream.Position = 0;
            _stream.CopyTo(stream);
            _stream.Position = _stream.Length;
        }

        public void SaveAppend(string path)
        {
            if (!_headerWritten)
                throw new InvalidOperationException();

            if (!_finished)
                Finish();

            using (var fs = new FileStream(path, FileMode.Append))
            {
                _stream.Position = 0;
                _stream.CopyTo(fs);
                _stream.Position = _stream.Length;
            }
        }

        public void SaveAt(string path, int sapIndex)
        {
            if (!_headerWritten)
                throw new InvalidOperationException();

            if (!_finished)
                Finish();

            if (!path.EndsWith(".sap", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException();
            }

            _stream.Position = 0;
            var newSample = _stream.ToArray();
            _stream.Position = _stream.Length;

            var sapFile = new SapFile(path);
            sapFile.WavFiles[sapIndex] = newSample;
            sapFile.Save(path);
        }

        public void AppendSilence(double length)
        {
            if (length <= 0)
                return;

            if (!_headerWritten)
            {
                _initialSilence += length;
                return;
            }

            var numBytes = AlignTime(in _header, length);
            if (numBytes <= 0)
                return;

            var bytes = new byte[4096];
            while (numBytes != 0)
            {
                var left = Math.Min(bytes.Length, numBytes);
                _stream.Write(bytes, 0, left);
                numBytes -= left;
            }
        }

        public WaveformBuilder Append(string path)
        {
            return Append(path, 0, double.NaN);
        }

        public WaveformBuilder Append(string path, double start, double end)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            Append(path, fs, start, end);
            return this;
        }

        public WaveformBuilder Append(string path, int sapIndex, double start, double end)
        {
            var sapFile = new SapFile(path);
            var wavFile = sapFile.WavFiles[sapIndex];
            var sapStream = new MemoryStream(wavFile);
            var wavStream = new MemoryStream();
            var decoder = new MSADPCMDecoder();
            decoder.Convert(sapStream, wavStream);
            wavStream.Position = 0;
            AppendWav(wavStream, start, end);
            return this;
        }

        public void Append(string path, Stream input) => Append(path, input, 0, double.NaN);

        public void Append(string path, Stream input, double start, double end)
        {
            try
            {
                if (path.EndsWith(".sap", StringComparison.OrdinalIgnoreCase))
                {
                    input.Position += 8;

                    var br = new BinaryReader(input);
                    var magic = br.ReadUInt32();
                    input.Position -= 4;
                    if (magic == 0x5367674F) // OGG
                    {
                        using (var ss = new SlicedStream(input, 8, input.Length - 8))
                        {
                            AppendOgg(ss, start, end);
                        }
                    }
                    else
                    {
                        var ms = new MemoryStream();
                        var decoder = new MSADPCMDecoder();
                        decoder.Convert(input, ms);

                        ms.Position = 0;
                        AppendWav(ms, start, end);
                    }
                }
                else if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                {
                    AppendOgg(input, start, end);
                }
                else
                {
                    AppendWav(input, start, end);
                }
            }
            catch (Exception ex)
            {
                throw new BioRandUserException($"Unable to process '{path}'. {ex.Message}");
            }
        }

        public void AppendWav(Stream input, double start, double end)
        {
            var initialPosition = input.Position;
            var br = new BinaryReader(input);
            var header = br.ReadStruct<WaveHeader>();
            if (header.wFormatTag == 2)
            {
                input.Position = initialPosition;
                var ms = new MemoryStream();
                var decoder = new MSADPCMDecoder();
                decoder.Convert(input, ms);
                ms.Position = 0;
                AppendWav(ms, start, end);
                return;
            }
            if (header.wBitsPerSample == 8)
            {
                input.Position = initialPosition;
                var ms = new MemoryStream();
                Convert8to16(ms, input);
                ms.Position = 0;
                AppendWav(ms, start, end);
                return;
            }

            var targetSampleRate = SampleRate != 0 ?
                (uint)SampleRate :
                _headerWritten ?
                    _header.nSamplesPerSec :
                    header.nSamplesPerSec;
            if (header.nSamplesPerSec != targetSampleRate)
            {
                input.Position = initialPosition;
                var ms = new MemoryStream();
                if (header.nChannels != 1)
                {
                    ConvertChannels(ms, input, 1);
                }
                else
                {
                    ConvertSampleRate(ms, input, targetSampleRate);
                }
                ms.Position = 0;
                AppendWav(ms, start, end);
                return;
            }

            if (header.nChannels != 1 && header.nChannels != 2)
                throw new NotSupportedException("Only mono or stereo sound can be converted.");

            if (!_headerWritten)
            {
                WriteHeader(in header);
            }

            var startOffset = AlignTime(in header, start);
            var endOffset = AlignTime(in header, end);
            var length = endOffset - startOffset;
            if (length > 0)
            {
                input.Position += startOffset;
                if (Volume == 1 && _header.nSamplesPerSec == header.nSamplesPerSec && _header.nChannels == header.nChannels)
                {
                    input.CopyAmountTo(_stream, length);
                }
                else
                {
                    var bw = new BinaryWriter(_stream);
                    if (header.nSamplesPerSec != _header.nSamplesPerSec)
                    {
                        if (header.nChannels != 1)
                            throw new NotSupportedException("Resampling not yet supported for stereo.");

                        var resampleStream = new MemoryStream();
                        bw = new BinaryWriter(resampleStream);
                    }
                    if (header.nChannels > _header.nChannels)
                    {
                        for (int i = 0; i < length; i += 4)
                        {
                            var left = br.ReadInt16();
                            var right = br.ReadInt16();
                            var sample = (short)((left + right) / 2);
                            bw.Write((short)(sample * Volume));
                        }
                    }
                    else if (header.nChannels < _header.nChannels)
                    {
                        for (int i = 0; i < length; i += 2)
                        {
                            var sampleIn = br.ReadInt16();
                            var sampleOut = (short)(sampleIn * Volume);
                            bw.Write(sampleOut);
                            bw.Write(sampleOut);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < length; i += 2)
                        {
                            var sample = br.ReadInt16();
                            bw.Write((short)(sample * Volume));
                        }
                    }
                    if (header.nSamplesPerSec != _header.nSamplesPerSec)
                    {
                        var inStream = bw.BaseStream;
                        inStream.Position = 0;
                        var inSamples = (int)(inStream.Length / 2);
                        var factor = (double)_header.nSamplesPerSec / header.nSamplesPerSec;
                        Resample(inStream, _stream, inSamples, factor);
                    }
                }
            }
        }

        public unsafe void AppendOgg(Stream input, double start, double end)
        {
            var streamOutput = new MemoryStream();
            var bw = new BinaryWriter(streamOutput);

            using (var vorbis = new VorbisReader(input))
            {
                var header = GetOggHeader(vorbis);
                streamOutput.Position = sizeof(WaveHeader);

                if (start != 0)
                    vorbis.SeekTo(TimeSpan.FromSeconds(start));

                // Stream samples from ogg
                var maxSamplesToRead = double.IsNaN(end) ? int.MaxValue : (int)(vorbis.Channels * vorbis.SampleRate * end);

                int readSamples;
                var readBuffer = new float[vorbis.Channels * vorbis.SampleRate / 8];
                while ((readSamples = vorbis.ReadSamples(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    var leftToRead = Math.Min(readSamples, maxSamplesToRead);
                    if (Volume == 1)
                    {
                        for (int i = 0; i < leftToRead; i++)
                        {
                            var value = (short)(readBuffer[i] * short.MaxValue);
                            bw.Write(value);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < leftToRead; i++)
                        {
                            var value = (short)(readBuffer[i] * short.MaxValue * Volume);
                            bw.Write(value);
                        }
                    }
                    maxSamplesToRead -= leftToRead;
                }

                header.nRiffLength = (uint)(streamOutput.Position - 8);
                header.nDataLength = (uint)(streamOutput.Position - sizeof(WaveHeader));

                streamOutput.Position = 0;
                bw.Write(header);
            }

            streamOutput.Position = 0;
            AppendWav(streamOutput, 0, double.NaN);
        }

        private static WaveHeader GetOggHeader(VorbisReader vorbis)
        {
            var header = new WaveHeader();
            header.nRiffMagic = g_riffMagic;
            header.nRiffLength = 0;
            header.nWaveMagic = g_waveMagic;
            header.nFormatMagic = g_fmtMagic;
            header.nFormatLength = 16;
            header.wFormatTag = 1;
            header.nChannels = (ushort)vorbis.Channels;
            header.nSamplesPerSec = (uint)vorbis.SampleRate;
            header.nAvgBytesPerSec = (uint)(vorbis.SampleRate * 16 * vorbis.Channels) / 8;
            header.nBlockAlign = (ushort)((16 * vorbis.Channels) / 8);
            header.wBitsPerSample = 16;
            header.wDataMagic = g_dataMagic;
            header.nDataLength = 0;
            return header;
        }

        private static int AlignTime(in WaveHeader header, double t)
        {
            var precise = double.IsNaN(t) ?
                (int)header.nDataLength :
                header.nAvgBytesPerSec * t;
            var result = ((int)precise / header.nBlockAlign) * header.nBlockAlign;
            if (result > header.nDataLength)
            {
                result = (int)header.nDataLength;
            }
            return result;
        }

        private unsafe void Convert8to16(Stream output, Stream input)
        {
            var bw = new BinaryWriter(output);
            var br = new BinaryReader(input);
            var header = br.ReadStruct<WaveHeader>();
            if (header.wBitsPerSample == 8)
            {
                var oldDataLength = (int)header.nDataLength;

                header.wBitsPerSample = 16;
                header.nDataLength *= 2;
                header.nAvgBytesPerSec *= 2;
                header.nBlockAlign *= 2;
                header.nRiffLength = (uint)(header.nDataLength + sizeof(WaveHeader) - 8);
                bw.Write(header);

                var data = br.ReadBytes(oldDataLength);
                for (int i = 0; i < data.Length; i++)
                {
                    bw.Write((short)((data[i] - 128) * 256));
                }
            }
            else
            {
                bw.Write(header);
                input.CopyAmountTo(output, header.nDataLength);
            }
        }

        private unsafe void ConvertChannels(Stream output, Stream input, int desiredChannels)
        {
            var bw = new BinaryWriter(output);
            var br = new BinaryReader(input);
            var header = br.ReadStruct<WaveHeader>();
            if (header.wFormatTag != 1)
            {
                throw new NotSupportedException("Unsupported .wav format for channel conversion.");
            }
            if (header.nChannels == 1 && desiredChannels == 2)
            {
                var oldDataLength = (int)header.nDataLength;

                header.nChannels = 2;
                header.nDataLength *= 2;
                bw.Write(header);

                var data = MemoryMarshal.Cast<byte, short>(new Span<byte>(br.ReadBytes(oldDataLength)));
                for (int i = 0; i < data.Length; i++)
                {
                    bw.Write(data[i]);
                    bw.Write(data[i]);
                }
            }
            else if (header.nChannels == 2 && desiredChannels == 1)
            {
                var oldDataLength = (int)header.nDataLength;

                header.nChannels = 1;
                header.nDataLength = (uint)((header.nDataLength / 2) & ~1);
                bw.Write(header);

                var data = MemoryMarshal.Cast<byte, short>(new Span<byte>(br.ReadBytes(oldDataLength)));
                for (int i = 0; i < header.nDataLength / 2; i++)
                {
                    var srcI = i * 2;
                    bw.Write((short)((data[srcI] + data[srcI + 1]) / 2));
                }
            }
            else if (header.nChannels == desiredChannels)
            {
                bw.Write(header);
                var data = br.ReadBytes((int)header.nDataLength);
                bw.Write(data);
            }
            else
            {
                throw new NotSupportedException($"Conversion from {header.nChannels} channels to {desiredChannels} not supported.");
            }
        }

        private unsafe void ConvertSampleRate(Stream output, Stream input, uint targetSampleRate)
        {
            var bw = new BinaryWriter(output);
            var br = new BinaryReader(input);
            var header = br.ReadStruct<WaveHeader>();
            if (header.wFormatTag != 1 ||
                header.wBitsPerSample != 16 ||
                header.nChannels != 1)
            {
                throw new NotSupportedException("Unsupported .wav format for resampling.");
            }
            if (header.nSamplesPerSec == targetSampleRate)
            {
                bw.Write(header);
                var data = br.ReadBytes((int)header.nDataLength);
                bw.Write(data);
            }
            else
            {
                var inSamples = (int)(header.nDataLength / 2);
                var factor = (double)targetSampleRate / header.nSamplesPerSec;
                var headerPos = output.Position;
                bw.Write(header);
                var dataPos = output.Position;
                Resample(input, output, inSamples, factor);
                var backupPos = output.Position;
                output.Position = headerPos;
                header.nSamplesPerSec = targetSampleRate;
                header.nDataLength = (uint)(backupPos - dataPos);
                bw.Write(header);
                output.Position = backupPos;
            }
        }

        private const int SINC_WINDOW_FXP = 15;

        private void Resample(Stream input, Stream output, int inSamples, double factor)
        {
            var br = new BinaryReader(input);
            var bw = new BinaryWriter(output);
            var outSamples = (int)(inSamples * factor);
            var rate = 1 / factor;
            var src = MemoryMarshal.Cast<byte, short>(br.ReadBytes(inSamples * 2));
            var dst = new short[outSamples];
            ResampleSinc(src, inSamples, dst, outSamples, rate, factor);
            for (int i = 0; i < outSamples; i++)
            {
                bw.Write(dst[i]);
            }
        }

        /// <remarks>
        /// Based on https://github.com/Aikku93/wav2vag/.
        /// </remarks>
        private static void ResampleSinc(ReadOnlySpan<short> src, int nInSamples, Span<short> dst, int nOutSamples, double rate, double ratio)
        {
            //! Generate the sinc sliding window
            //! This window is NOT symmetric so we can't cheat :(
            const int LP_FIR_ORDER = 33, LP_FIR_HALF_ORDER = LP_FIR_ORDER / 2; //! LP_FIR_ORDER must be odd
            const int FRAC_BITS = 12, FRAC_SCALE = 1 << FRAC_BITS;
            var sincWin = new Span<short>(new short[FRAC_SCALE * LP_FIR_ORDER]);
            for (var n = 0; n < FRAC_SCALE; n++)
            {
                var win = sincWin.Slice(n * LP_FIR_ORDER);
                GenerateSincWindow(win, -LP_FIR_HALF_ORDER, +LP_FIR_HALF_ORDER, n * (1.0 / FRAC_SCALE), LP_FIR_ORDER, (ratio < 1.0) ? ratio : 1.0);
            }

            //! Filter and then free the sinc window
            int iSrc = 0;
            double mu = 0.0;
            for (var n = 0; n < nOutSamples; n++)
            {
                var winOffset = (int)(mu * FRAC_SCALE) * LP_FIR_ORDER + LP_FIR_HALF_ORDER;
                int s = 0;
                for (var k = -LP_FIR_HALF_ORDER; k <= LP_FIR_HALF_ORDER; k++)
                {
                    var srcValue = 0;
                    var srcOffset = iSrc + k;
                    if (srcOffset >= 0 && srcOffset < src.Length)
                        srcValue = src[srcOffset];
                    s += sincWin[winOffset + k] * srcValue;
                }
                s = (s + (1 << SINC_WINDOW_FXP) / 2 - ((s < 0) ? 1 : 0)) >> SINC_WINDOW_FXP;
                dst[n] = Clip16(s);
                mu += rate;
                iSrc += (int)mu;
                mu -= (int)mu;
            }
        }

        private static void GenerateSincWindow(Span<short> dst, int nMin, int nMax, double xOfs, int order, double fc)
        {
            //! This is a Nuttall-windowed sinc window
            var i = 0;
            for (var n = nMin; n <= nMax; n++)
            {
                double sinc;
                {
                    double x = (n * fc - xOfs) * Math.PI;
                    sinc = x != 0 ? (Math.Sin(x) / x) : 1.0;
                }
                double nuttall;
                {
                    double x = n / (double)order + 0.5;
                    nuttall = 0.355768;
                    nuttall -= 0.487396 * Math.Cos(x * 2.0 * Math.PI);
                    nuttall += 0.144232 * Math.Cos(x * 4.0 * Math.PI);
                    nuttall -= 0.012604 * Math.Cos(x * 6.0 * Math.PI);
                }
                var v = (int)Math.Round(sinc * fc * nuttall * (1 << SINC_WINDOW_FXP));
                dst[i++] = Clip16(v);
            }
        }

        private static short Clip16(int x)
        {
            return (short)Math.Max(short.MinValue, Math.Min(x, short.MaxValue));
        }
    }
}
