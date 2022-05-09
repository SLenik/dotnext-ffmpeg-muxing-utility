using System;
using System.IO;
using FFmpeg.AutoGen;

namespace FFmpegSimpleMuxer
{
    unsafe class Program
    {
        private static AVFormatContext* _pAvFormatInputFileContext = null;
        private static AVFormatContext* _pAvFormatOutputFileContext = null;

        // based on
        // https://stackoverflow.com/questions/16282600/how-to-use-libavformat-to-concat-2-video-files-with-same-codec-re-muxing
        // https://github.com/FFmpeg/FFmpeg/blob/master/doc/examples/remuxing.c
        private static void Main(string[] args)
        {
            ffmpeg.RootPath = $".{Path.DirectorySeparatorChar}ffmpeg-lib";

            var (inFileName, outFileName) = ParseParametersOrExit(args);
            var errorCode = 0;

            fixed (AVFormatContext** ppAvFormatInputFileContext = &_pAvFormatInputFileContext)
            fixed (AVFormatContext** ppAvFormatOutputFileContext = &_pAvFormatOutputFileContext)
            {
                // method can produce memory leaks if it is called from several threads concurrently. So if your app is multithreaded, please use lock on some static variable
                errorCode = ffmpeg.avformat_open_input(ppAvFormatInputFileContext, inFileName, null, null);
                ExitOnFfmpegError(errorCode, $"unable to open input file {inFileName}");

                errorCode = ffmpeg.avformat_alloc_output_context2(ppAvFormatOutputFileContext, null, null, outFileName);
                ExitOnFfmpegError(errorCode, $"unable to alloc context for output file {outFileName}");
            }

            CopyStreamInfo(_pAvFormatOutputFileContext, _pAvFormatInputFileContext);

            // method can produce memory leaks if it is called from several threads concurrently. So if your app is multithreaded, please use lock on some static variable
            errorCode = ffmpeg.avio_open(&_pAvFormatOutputFileContext->pb, outFileName, ffmpeg.AVIO_FLAG_WRITE);
            ExitOnFfmpegError(errorCode, $"error while trying to opening file {outFileName} for writing");

            errorCode = ffmpeg.avformat_write_header(_pAvFormatOutputFileContext, null);
            ExitOnFfmpegError(errorCode, $"error while trying to write header of file {outFileName}");

            CopyDataPackets(_pAvFormatOutputFileContext, _pAvFormatInputFileContext);

            errorCode = ffmpeg.av_write_trailer(_pAvFormatOutputFileContext);
            ExitOnFfmpegError(errorCode, $"error while trying to write trailer of file {outFileName}");

            Cleanup();
        }

        private static void CopyStreamInfo(AVFormatContext* pAvFormatOutputFileContext, AVFormatContext* pAvFormatInputFileContext)
        {
            for (var i = 0; i < pAvFormatInputFileContext->nb_streams; i++)
            {
                var pAvInputStream = pAvFormatInputFileContext->streams[i];

                PrintStreamInfo(i, pAvInputStream);

                var pAvOutputStream = ffmpeg.avformat_new_stream(pAvFormatOutputFileContext, null);

                if (pAvOutputStream is null)
                    ExitWithErrorText($"Unable to create new stream #{i} for output file");

                var errorCode = ffmpeg.avcodec_parameters_copy(pAvOutputStream->codecpar, pAvInputStream->codecpar);
                ExitOnFfmpegError(errorCode, $"error while trying to copy stream #{i} parameters");

                uint codecTag = 0;
                if (ffmpeg.av_codec_get_tag2(pAvFormatOutputFileContext->oformat->codec_tag,
                        pAvInputStream->codecpar->codec_id, &codecTag) == 0)
                {
                    Console.WriteLine(
                        $"Warning: could not find codec tag for codec id {pAvInputStream->codecpar->codec_id}, default to 0.");
                }

                pAvOutputStream->codecpar->codec_tag = codecTag;

                for (var j = 0; j < pAvInputStream->nb_side_data; j++)
                {
                    var avPacketSideData = pAvInputStream->side_data[j];

                    var pAvOutPacketSideData =
                        ffmpeg.av_stream_new_side_data(pAvOutputStream, avPacketSideData.type, avPacketSideData.size);

                    if (pAvOutPacketSideData is null)
                        ExitWithErrorText($"Unable to allocate side data structure for stream #{i} of output file");

                    Buffer.MemoryCopy(avPacketSideData.data, pAvOutPacketSideData, avPacketSideData.size,
                        avPacketSideData.size);
                }

                ffmpeg.av_dict_copy(&pAvOutputStream->metadata, pAvInputStream->metadata, ffmpeg.AV_DICT_APPEND);
            }
        }

        private static void CopyDataPackets(AVFormatContext* pAvFormatOutputFileContext, AVFormatContext* pAvFormatInputFileContext)
        {
            AVPacket avPacket;
            while (true)
            {
                ffmpeg.av_packet_unref(&avPacket);
                var errorCode = ffmpeg.av_read_frame(pAvFormatInputFileContext, &avPacket);

                if (errorCode == ffmpeg.AVERROR_EOF)
                {
                    break;
                }
                else
                {
                    ExitOnFfmpegError(errorCode, $"error while trying to read packet from input file");
                }

                if (avPacket.stream_index >= pAvFormatInputFileContext->nb_streams)
                {
                    Console.WriteLine(
                        $"Warning: found new stream #{avPacket.stream_index} in input file. It will be ignored.");
                    continue;
                }

                if (avPacket.flags.HasFlag(ffmpeg.AV_PKT_FLAG_CORRUPT))
                {
                    Console.WriteLine(
                        $"Warning: corrupt packet found in stream #{avPacket.stream_index}.");
                }

                var pAvInputStream = pAvFormatInputFileContext->streams[avPacket.stream_index];
                var pAvOutputStream = pAvFormatOutputFileContext->streams[avPacket.stream_index];

                avPacket.pts = ffmpeg.av_rescale_q_rnd(avPacket.pts, pAvInputStream->time_base,
                    pAvOutputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                avPacket.dts = ffmpeg.av_rescale_q_rnd(avPacket.dts, pAvInputStream->time_base,
                    pAvOutputStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                avPacket.duration =
                    ffmpeg.av_rescale_q(avPacket.duration, pAvInputStream->time_base, pAvOutputStream->time_base);
                avPacket.pos = -1;

                ffmpeg.av_write_frame(pAvFormatOutputFileContext, &avPacket);
            }

            ffmpeg.av_packet_unref(&avPacket);
        }

        private static void PrintStreamInfo(int streamIndex, AVStream* pAvStream)
        {
            // There is av_dump_format function that produces almost the same result =)

            Console.WriteLine($"Stream {streamIndex}");
            Console.WriteLine($"\tType: {pAvStream->codecpar->codec_type}");
            Console.WriteLine($"\tCodecId: {pAvStream->codecpar->codec_id}");

            var pCodecDescriptor = ffmpeg.avcodec_descriptor_get(pAvStream->codecpar->codec_id);
            string codecName, codecLongName;
            if (pCodecDescriptor is not null)
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


        private static (string, string) ParseParametersOrExit(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("usage: <file to read> <file to write>");
                Environment.Exit(-1);
            }

            var inFileName = args[0];
            if (!FileCanBeRead(inFileName))
            {
                Console.WriteLine($"cannot read file {inFileName} or file is empty");
                Environment.Exit(-1);
            }

            var outFileName = args[1];
            if (!FileCanBeWritten(outFileName))
            {
                Console.WriteLine($"cannot write to file {outFileName}");
                Environment.Exit(-1);
            }

            return (inFileName, outFileName);
        }

        private static bool FileCanBeRead(string fileName)
        {
            const int bufferSize = 4;

            using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
                FileOptions.SequentialScan);

            var buffer = new byte[bufferSize];

            return fs.Read(buffer, 0, bufferSize) == bufferSize;
        }

        private static bool FileCanBeWritten(string fileName)
        {
            using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Write);
            return fs.CanWrite;
        }


        private static void Cleanup()
        {
            if (_pAvFormatInputFileContext is not null)
            {
                fixed (AVFormatContext** ppAvFormatFileContext = &_pAvFormatInputFileContext)
                    ffmpeg.avformat_close_input(ppAvFormatFileContext);
            }

            if (_pAvFormatOutputFileContext is not null)
            {
                fixed (AVFormatContext** ppAvFormatFileContext = &_pAvFormatOutputFileContext)
                    ffmpeg.avformat_close_input(ppAvFormatFileContext);
            }
        }

        private static void ExitOnFfmpegError(int ffmpegResultCode, string errorDescription)
        {
            if (ffmpegResultCode >= 0)
                return;

            var errorText = FfmpegHelper.GetErrorText(ffmpegResultCode);

            ExitWithErrorText($"{errorDescription}. Ffmpeg error text: {errorText}");
        }

        private static void ExitWithErrorText(string errorText)
        {
            Console.WriteLine(errorText);

            Cleanup();

            Environment.Exit(-1);
        }
    }
}
