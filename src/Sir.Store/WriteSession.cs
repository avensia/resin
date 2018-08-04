﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Sir.Store
{
    public class WriteSession : Session
    {
        private readonly IDictionary<string, VectorNode> _dirty;
        private readonly ValueWriter _vals;
        private readonly ValueWriter _keys;
        private readonly DocWriter _docs;
        private readonly ValueIndexWriter _valIx;
        private readonly ValueIndexWriter _keyIx;
        private readonly DocIndexWriter _docIx;

        public WriteSession(string directory, ulong collectionId, SessionFactory sessionFactory) 
            : base(directory, collectionId, sessionFactory)
        {
            _dirty = new Dictionary<string, VectorNode>();

            ValueStream = sessionFactory.WritableValueStream;
            KeyStream = sessionFactory.CreateAppendStream(string.Format("{0}.key", collectionId));
            DocStream = sessionFactory.CreateAppendStream(string.Format("{0}.docs", collectionId));
            ValueIndexStream = sessionFactory.WritableValueIndexStream;
            KeyIndexStream = sessionFactory.CreateAppendStream(string.Format("{0}.kix", collectionId));
            DocIndexStream = sessionFactory.CreateAppendStream(string.Format("{0}.dix", collectionId));
            PostingsStream = sessionFactory.CreateReadWriteStream(string.Format("{0}.pos", collectionId));
            VectorStream = sessionFactory.CreateAppendStream(string.Format("{0}.vec", collectionId));
            Index = sessionFactory.GetIndex(collectionId);

            _vals = new ValueWriter(ValueStream);
            _keys = new ValueWriter(KeyStream);
            _docs = new DocWriter(DocStream);
            _valIx = new ValueIndexWriter(ValueIndexStream);
            _keyIx = new ValueIndexWriter(KeyIndexStream);
            _docIx = new DocIndexWriter(DocIndexStream);
        }

        public void Write(IEnumerable<IDictionary> data, ITokenizer tokenizer)
        {
            foreach (var model in data)
            {
                var docId = _docIx.GetNextDocId();
                var docMap = new List<(uint keyId, uint valId)>();

                foreach (var key in model.Keys)
                {
                    var keyStr = key.ToString();
                    var keyHash = keyStr.ToHash();
                    var fieldIndex = GetIndex(keyHash);
                    var val = (IComparable)model[key];
                    var str = val as string;
                    var fullTextTokens = new List<Term>();
                    uint keyId, valId;

                    if (str != null) //TODO: implement numeric index
                    {
                        foreach (var token in tokenizer.Tokenize(str))
                        {
                            fullTextTokens.Add(new Term(keyStr, token));
                        }
                    }

                    if (fieldIndex == null)
                    {
                        // We have a new key!

                        // store key
                        var keyInfo = _keys.Append(keyStr);
                        keyId = _keyIx.Append(keyInfo.offset, keyInfo.len, keyInfo.dataType);
                        SessionFactory.AddKey(keyHash, keyId);

                        // add new index to global in-memory tree
                        fieldIndex = new VectorNode();
                        Index.Add(keyId, fieldIndex);
                    }
                    else
                    {
                        keyId = SessionFactory.GetKey(keyHash);
                    }

                    // store value
                    var valInfo = _vals.Append(val);
                    valId = _valIx.Append(valInfo.offset, valInfo.len, valInfo.dataType);

                    // store refs to keys and values
                    docMap.Add((keyId, valId));

                    foreach (var token in fullTextTokens)
                    {
                        // add token and postings to index
                        var strVal = (string)token.Value;
                        var match = fieldIndex.ClosestMatch(strVal);
                        match.Add(strVal, docId);
                    }

                    var indexName = string.Format("{0}.{1}", CollectionId, keyId);
                    if (!_dirty.ContainsKey(indexName))
                    {
                        _dirty.Add(indexName, fieldIndex);
                    }
                }

                var docMeta = _docs.Append(docMap);
                _docIx.Append(docMeta.offset, docMeta.length);
            }
        }

        public override void Dispose()
        {
            foreach (var node in _dirty)
            {
                var fn = Path.Combine(Dir, node.Key + ".ix");
                var fileMode = File.Exists(fn) ? FileMode.Truncate : FileMode.Append;

                using (var stream = new FileStream(fn, fileMode, FileAccess.Write, FileShare.None))
                {
                    node.Value.Serialize(stream, VectorStream, PostingsStream);
                }
            }

            ValueStream.Flush();
            KeyStream.Flush();
            DocStream.Flush();
            ValueIndexStream.Flush();
            KeyIndexStream.Flush();
            DocIndexStream.Flush();
            PostingsStream.Flush();
            VectorStream.Flush();

            base.Dispose();
        }
    }
}
