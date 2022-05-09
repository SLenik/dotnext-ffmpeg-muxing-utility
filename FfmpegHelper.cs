using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FFmpegSimpleMuxer
{
    static unsafe class FfmpegHelper
    {
        /// <summary>
        /// Test if <paramref name="value"/> contains <paramref name="flag"/>
        /// </summary>
        public static bool HasFlag(this int value, int flag)
        {
            return (value & flag) > 0;
        }

        /// <summary>
        /// Converts pointer to byte array to managed string.
        /// </summary>
        /// <param name="pByteArray">Pointer to byte array with non-unicode chars.</param>
        /// <returns>Managed string.</returns>
        public static string GetString(void* pByteArray)
        {
            return new string((sbyte*)pByteArray);
        }

        /// <summary>
        /// Creates an enumerator which can be used to iterate over all antries of <see cref="AVDictionary"/>.
        /// </summary>
        /// <param name="source">Pointer to <see cref="AVDictionary"/> which we want to be iterated.</param>
        public static IEnumerable<AVDictionaryEntry> GetEnumerator(AVDictionary* source)
        {
            return new AvDictionaryIterator(source);
        }

        /// <summary>
        /// Converts ffmpeg error code to managed string.
        /// </summary>
        /// <param name="errorCode">Ffmpeg error code.</param>
        /// <param name="errorStringBufferSize">Intermediate buffer to store string.</param>
        /// <returns>Managed string with error description.</returns>
        public static string GetErrorText(int errorCode, int errorStringBufferSize = 1024)
        {
            if (errorCode >= 0) return "";

            var buffer = IntPtr.Zero;
            try
            {
                Marshal.AllocHGlobal(errorStringBufferSize);
                ffmpeg.av_make_error_string((byte*)buffer.ToPointer(), (uint)errorStringBufferSize, errorCode);
                return GetString(buffer.ToPointer());
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }
}
