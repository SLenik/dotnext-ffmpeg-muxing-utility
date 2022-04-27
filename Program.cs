using FFmpeg.AutoGen;
using System;
using System.IO;

namespace FFmpegSimpleMuxer
{
    unsafe class Program
    {
        static AVFormatContext* _pAvFormatFileContext = null;

        private static void Main(string[] args)
        {
            ffmpeg.RootPath = $".{Path.DirectorySeparatorChar}ffmpeg-lib";

            var inFileName = ParseParametersOrExit(args);

            fixed (AVFormatContext** ppAvFormatFileContext = &_pAvFormatFileContext)
            {
                // method can produce memory leaks if it is called from several threads concurrently. So if your app is multithreaded, please use lock on some static variable
                var errorCode = ffmpeg.avformat_open_input(ppAvFormatFileContext, inFileName, null, null);
                ExitIfResultIsError(errorCode, $"unable to open file {inFileName}");

                for (var i = 0; i < _pAvFormatFileContext->nb_streams; i++)
                {
                    var pAvStream = _pAvFormatFileContext->streams[i];
                    Console.WriteLine($"Stream {i}");
                    Console.WriteLine($"\tType: {pAvStream->codecpar->codec_type}");
                    Console.WriteLine($"\tCodecId: {pAvStream->codecpar->codec_id}");

                    var pCodecDescriptor = ffmpeg.avcodec_descriptor_get(pAvStream->codecpar->codec_id);
                    string codecName, codecLongName;
                    if (pCodecDescriptor != null)
                    {
                        codecName = FfmpegHelper.GetString(pCodecDescriptor->name);
                        codecLongName = FfmpegHelper.GetString(pCodecDescriptor->long_name);
                    }
                    else
                    {
                        codecName = codecLongName = "Unknown";
                    }
                    Console.WriteLine($"\tCodec name: {codecName}");
                    Console.WriteLine($"\tCodec long name: {codecLongName}");

                    Console.WriteLine($"\tBitrate: {pAvStream->codecpar->bit_rate}");

                    if (pAvStream->duration > 0)
                    {
                        var durationInSeconds = pAvStream->duration * ffmpeg.av_q2d(pAvStream->time_base);
                        Console.WriteLine($"\tDuration: {TimeSpan.FromSeconds(durationInSeconds)}");
                    }

                    if (pAvStream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        Console.WriteLine($"\tWidth: {pAvStream->codecpar->width}");
                        Console.WriteLine($"\tHeight: {pAvStream->codecpar->height}");

                        var avRationalAspectRatio = pAvStream->codecpar->sample_aspect_ratio;
                        Console.WriteLine($"\tSample aspect ratio: {avRationalAspectRatio.num} / {avRationalAspectRatio.den}");

                        Console.WriteLine($"\tLevel: {pAvStream->codecpar->level}");
                    }
                    else if (pAvStream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    {
                        Console.WriteLine($"\tChannels: {pAvStream->codecpar->channels}");
                        Console.WriteLine($"\tSample rate: {pAvStream->codecpar->sample_rate}");
                        Console.WriteLine($"\tFrame size: {pAvStream->codecpar->frame_size}");
                    }

                    foreach (var avDictionaryEntry in FfmpegHelper.GetEnumerator(pAvStream->metadata))
                    {
                        var key = FfmpegHelper.GetString(avDictionaryEntry.key);
                        var value = FfmpegHelper.GetString(avDictionaryEntry.value);
                        Console.WriteLine($"\t\t{key}: {value}");
                    }
                }
            }

            Cleanup();
        }

        private static string ParseParametersOrExit(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("provide file to read");
                Environment.Exit(-1);
            }

            const int bufferSize = 4;
            var inFileName = args[0];

            using var fs = new FileStream(inFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
                FileOptions.SequentialScan);

            var buffer = new byte[bufferSize];

            if (fs.Read(buffer, 0, bufferSize) != bufferSize)
            {
                Console.WriteLine("cannot read file or file is empty");
                Environment.Exit(-1);
            }

            return inFileName;
        }

        private static void Cleanup()
        {
            if (_pAvFormatFileContext != null)
            {
                fixed (AVFormatContext** ppAvFormatFileContext = &_pAvFormatFileContext)
                    ffmpeg.avformat_close_input(ppAvFormatFileContext);
            }
        }

        private static void ExitIfResultIsError(int resultCode, string errorDescription)
        {
            if (resultCode >= 0)
                return;

            var errorText = FfmpegHelper.GetErrorText(resultCode);

            Console.WriteLine($"{errorDescription}. Ffmpeg error text: {errorText}");

            Cleanup();

            Environment.Exit(resultCode);
        }
    }
}
