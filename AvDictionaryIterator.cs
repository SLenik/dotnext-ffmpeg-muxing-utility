#nullable enable

using FFmpeg.AutoGen;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace FFmpegSimpleMuxer
{
    internal sealed unsafe class AvDictionaryIterator : IEnumerable<AVDictionaryEntry>, IEnumerator<AVDictionaryEntry>
    {
        private readonly int _threadId;
        private readonly AVDictionary* _source;
        private AVDictionaryEntry* _current = null;
        private State _state;

        public AvDictionaryIterator(AVDictionary* source)
        {
            _threadId = Environment.CurrentManagedThreadId;
            Debug.Assert(source != null);
            _source = source;
            _state = State.New;
        }


        /// <inheritdoc />
        public AVDictionaryEntry Current => _current != null ? *_current : default;

        /// <summary>
        /// Makes a shallow copy of this iterator.
        /// </summary>
        /// <remarks>
        /// This method is called if <see cref="GetEnumerator"/> is called more than once.
        /// </remarks>
        public AvDictionaryIterator Clone() => new AvDictionaryIterator(_source);

        /// <inheritdoc />
        public IEnumerator<AVDictionaryEntry> GetEnumerator()
        {
            var enumerator = _state == State.New && _threadId == Environment.CurrentManagedThreadId
                ? this
                : Clone();
            enumerator._state = State.InProgress;
            return enumerator;
        }

        /// <inheritdoc />
        public bool MoveNext()
        {
            _current = ffmpeg.av_dict_get(_source, "", _current, ffmpeg.AV_DICT_IGNORE_SUFFIX);
            _state = State.InProgress;

            if (_current != null)
                return true;

            Dispose();
            return false;
        }


        /// <inheritdoc />
        object IEnumerator.Current => Current;

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        void IEnumerator.Reset() => throw new NotSupportedException();

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            //GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_state == State.Disposed)
                return;

            if (disposing)
            {
                // dispose managed resources here
            }

            // dispose unmanaged resources here
            _state = State.Disposed;
        }

        private enum State
        {
            New,
            InProgress,
            Disposed = -1
        }
    }
}
