﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Sir.Store
{
    public class ColumnSerializer : ILogger, IDisposable
    {
        private readonly long _keyId;
        private readonly ulong _collectionId;
        private readonly RemotePostingsWriter _postingsWriter;
        private readonly SessionFactory _sessionFactory;
        private static readonly object _indexFileSync = new object();
        private readonly PageIndexWriter _pageIndexWriter;
        private readonly Stream _ixStream;

        public ColumnSerializer(ulong collectionId, long keyId, SessionFactory sessionFactory, RemotePostingsWriter postingsWriter, string ixFileExtension = "ix", string pageFileExtension = "ixp")
        {
            _keyId = keyId;
            _collectionId = collectionId;
            _postingsWriter = postingsWriter;
            _sessionFactory = sessionFactory;

            var pixFileName = Path.Combine(_sessionFactory.Dir, string.Format("{0}.{1}.{2}", _collectionId, keyId, pageFileExtension));
            var ixFileName = Path.Combine(_sessionFactory.Dir, string.Format("{0}.{1}.{2}", _collectionId, keyId, ixFileExtension));

            _pageIndexWriter = new PageIndexWriter(_sessionFactory.CreateAsyncAppendStream(pixFileName));
            _ixStream = _sessionFactory.CreateAppendStream(ixFileName);
        }

        public async Task CreateColumnSegment(VectorNode column)
        {
            var time = Stopwatch.StartNew();

            await _postingsWriter.Write(column);

            var page = column.SerializeTree(_ixStream);

            await _ixStream.FlushAsync();
            await _pageIndexWriter.WriteAsync(page.offset, page.length);
            await _pageIndexWriter.FlushAsync();

            var size = column.Size();

            this.Log("serialized column {0} in {1}. weight {2} depth {3} width {4} (avg depth {5})",
                _keyId, time.Elapsed, column.Weight, size.depth, size.width, size.avgDepth);
        }

        public void Dispose()
        {
            _ixStream.Dispose();
            _pageIndexWriter.Dispose();
        }
    }
}