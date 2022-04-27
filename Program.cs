using System;
using System.IO;
using FFmpeg.AutoGen;

namespace FFmpegSimpleMuxer
{
    class Program
    {
        static void Main(string[] args)
        {
            ffmpeg.RootPath = $".{Path.DirectorySeparatorChar}ffmpeg-lib";
            Console.WriteLine(ffmpeg.avformat_configuration());
        }
    }
}
