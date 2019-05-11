﻿using Sir.Store;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Sir
{
    /// <summary>
    /// Perform calculations on sparse vectors.
    /// </summary>
    public static class VectorOperations
    {
        public static void DeserializeUnorderedFile(
            Stream indexStream,
            Stream vectorStream,
            VectorNode root,
            (float identicalAngle, float foldAngle) similarity)
        {
            var buf = new byte[VectorNode.BlockSize];
            int read = indexStream.Read(buf);

            while (read == VectorNode.BlockSize)
            {
                byte terminator = 2;
                var node = DeserializeNode(buf, vectorStream, ref terminator);

                if (node.VectorOffset > -1)
                    root.Add(node, similarity);

                read = indexStream.Read(buf);
            }
        }

        public static void DeserializeTree(
            Stream indexStream,
            Stream vectorStream,
            long indexLength,
            VectorNode root,
            (float identicalAngle, float foldAngle) similarity)
        {
            int read = 0;
            var buf = new byte[VectorNode.BlockSize];

            while (read < indexLength)
            {
                indexStream.Read(buf);

                byte terminator = 2;
                var node = DeserializeNode(buf, vectorStream, ref terminator);

                if (node.VectorOffset > -1)
                    root.Add(node, similarity);

                read += VectorNode.BlockSize;
            }
        }

        public static VectorNode DeserializeTree(Stream indexStream, Stream vectorStream, long indexLength)
        {
            VectorNode root = new VectorNode();
            VectorNode cursor = root;
            var tail = new Stack<VectorNode>();
            byte terminator = 2;
            int read = 0;
            var buf = new byte[VectorNode.BlockSize];

            while (read < indexLength)
            {
                indexStream.Read(buf);

                var node = DeserializeNode(buf, vectorStream, ref terminator);

                if (node.Terminator == 0) // there is both a left and a right child
                {
                    cursor.Left = node;
                    tail.Push(cursor);
                }
                else if (node.Terminator == 1) // there is a left but no right child
                {
                    cursor.Left = node;
                }
                else if (node.Terminator == 2) // there is a right but no left child
                {
                    cursor.Right = node;
                }
                else // there are no children
                {
                    if (tail.Count > 0)
                    {
                        tail.Pop().Right = node;
                    }
                }

                cursor = node;
                read += VectorNode.BlockSize;
            }

            var right = root.Right;

            right.DetachFromAncestor();

            return right;
        }

        public static VectorNode DeserializeNode(byte[] buf, MemoryMappedViewAccessor vectorView, ref byte terminator)
        {
            // Deserialize node
            var vecOffset = BitConverter.ToInt64(buf, 0);
            var postingsOffset = BitConverter.ToInt64(buf, sizeof(long));
            var vectorCount = BitConverter.ToInt32(buf, sizeof(long) + sizeof(long));
            var weight = BitConverter.ToInt32(buf, sizeof(long) + sizeof(long) + sizeof(int));

            // Deserialize term vector
            var vec = new SortedList<long, int>(vectorCount);
            var vecBuf = new byte[vectorCount * VectorNode.ComponentSize];

            vectorView.ReadArray(vecOffset, vecBuf, 0, vecBuf.Length);

            var offs = 0;

            for (int i = 0; i < vectorCount; i++)
            {
                var key = BitConverter.ToInt64(vecBuf, offs);
                var val = BitConverter.ToInt32(vecBuf, offs + sizeof(long));

                vec.Add(key, val);

                offs += VectorNode.ComponentSize;
            }

            // Create node
            var node = new VectorNode(vec);

            node.PostingsOffset = postingsOffset;
            node.VectorOffset = vecOffset;
            node.Terminator = terminator;
            node.Weight = weight;

            terminator = buf[buf.Length - 1];

            return node;
        }

        public static VectorNode DeserializeNode(byte[] nodeBuffer, Stream vectorStream, ref byte terminator)
        {
            // Deserialize node
            var vecOffset = BitConverter.ToInt64(nodeBuffer, 0);
            var postingsOffset = BitConverter.ToInt64(nodeBuffer, sizeof(long));
            var vectorCount = BitConverter.ToInt32(nodeBuffer, sizeof(long) + sizeof(long));
            var weight = BitConverter.ToInt32(nodeBuffer, sizeof(long) + sizeof(long) + sizeof(int));

            return DeserializeNode(vecOffset, postingsOffset, vectorCount, weight, vectorStream, ref terminator);
        }

        public static VectorNode DeserializeNode(
            long vecOffset,
            long postingsOffset,
            int componentCount,
            int weight,
            Stream vectorStream,
            ref byte terminator)
        {
            // Create node
            var node = new VectorNode(postingsOffset, vecOffset, terminator, weight, componentCount);

            node.Vector = DeserializeVector(node.VectorOffset, node.ComponentCount, vectorStream);

            return node;
        }

        public static SortedList<long, int> DeserializeVector(long vectorOffset, int componentCount, Stream vectorStream)
        {
            if (vectorOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vectorOffset));
            }

            if (vectorStream == null)
            {
                throw new ArgumentNullException(nameof(vectorStream));
            }

            // Deserialize term vector
            var vec = new SortedList<long, int>(componentCount);
            Span<byte> vecBuf = stackalloc byte[componentCount * VectorNode.ComponentSize];

            vectorStream.Seek(vectorOffset, SeekOrigin.Begin);
            vectorStream.Read(vecBuf);

            var offs = 0;

            for (int i = 0; i < componentCount; i++)
            {
                var key = BitConverter.ToInt64(vecBuf.Slice(offs, sizeof(long)));
                var val = BitConverter.ToInt32(vecBuf.Slice(offs + sizeof(long), sizeof(int)));

                vec.Add(key, val);

                offs += VectorNode.ComponentSize;
            }

            return vec;
        }

        public static SortedList<long, int> DeserializeVector(long vectorOffset, int componentCount, MemoryMappedViewAccessor vectorView)
        {
            if (vectorOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vectorOffset));
            }

            if (vectorView == null)
            {
                throw new ArgumentNullException(nameof(vectorView));
            }

            // Deserialize term vector
            var vec = new SortedList<long, int>(componentCount);
            Span<byte> vecBuf = stackalloc byte[componentCount * VectorNode.ComponentSize];
            var offs = vectorOffset;

            for (int i = 0; i < componentCount; i++)
            {
                var key = vectorView.ReadInt64(offs);
                var val = vectorView.ReadInt32(offs + sizeof(long));

                vec.Add(key, val);

                offs += VectorNode.ComponentSize;
            }

            return vec;
        }

        public static async Task<SortedList<long, int>> DeserializeVectorAsync(long vectorOffset, Stream vectorStream)
        {
            if (vectorStream == null)
            {
                throw new ArgumentNullException(nameof(vectorStream));
            }

            var vec = new SortedList<long, int>();

            if (vectorOffset > 0)
            {
                vectorStream.Seek(vectorOffset, SeekOrigin.Begin);
            }

            var buf = new byte[sizeof(long) + sizeof(int)];

            var read = await vectorStream.ReadAsync(buf);

            while (read > 0)
            {
                vec.Add(BitConverter.ToInt64(buf), BitConverter.ToInt32(buf, sizeof(long)));

                read = await vectorStream.ReadAsync(buf);
            }

            return vec;
        }

        public static async Task<long> SerializeAsync(this SortedList<long, int> vec, Stream stream)
        {
            var pos = stream.Position;

            foreach (var kvp in vec)
            {
                await stream.WriteAsync(BitConverter.GetBytes(kvp.Key), 0, sizeof(long));
                await stream.WriteAsync(BitConverter.GetBytes(kvp.Value), 0, sizeof(int));
            }

            return pos;
        }

        public static long Serialize(this SortedList<long, int> vec, Stream stream)
        {
            var pos = stream.Position;

            foreach (var kvp in vec)
            {
                stream.Write(BitConverter.GetBytes(kvp.Key));
                stream.Write(BitConverter.GetBytes(kvp.Value));
            }

            return pos;
        }

        public static float CosAngle(this SortedList<long, int> vec1, SortedList<long, int> vec2)
        {
            long dotProduct = Dot(vec1, vec2);
            long dotSelf1 = vec1.DotSelf();
            long dotSelf2 = vec2.DotSelf();

            return (float) (dotProduct / (Math.Sqrt(dotSelf1) * Math.Sqrt(dotSelf2)));
        }

        public static long Dot(this SortedList<long, int> vec1, SortedList<long, int> vec2)
        {
            if (ReferenceEquals(vec1, vec2))
            {
                return DotSelf(vec1);
            }

            long product = 0;
            var source = vec1.Count < vec2.Count ? vec1 : vec2;
            var target = ReferenceEquals(vec1, source) ? vec2 : vec1;

            foreach (var component1 in source)
            {
                int component2;

                if (target.TryGetValue(component1.Key, out component2))
                {
                    product += (component1.Value * component2);
                }
            }
            
            return product;
        }

        public static long DotSelf(this SortedList<long, int> vec)
        {
            long product = 0;

            foreach (var component in vec.Values)
            {
                product += (component * component);
            }

            return product;
        }

        public static SortedList<long, int> Add(this SortedList<long, int> vec1, SortedList<long, int> vec2)
        {
            var result = new SortedList<long, int>();

            foreach (var x in vec1)
            {
                int val;

                if (vec2.TryGetValue(x.Key, out val) && val < int.MaxValue)
                {
                    var v = val + x.Value;

                    result[x.Key] = v;
                }
                else
                {
                    result[x.Key] = x.Value;
                }
            }

            foreach (var x in vec2)
            {
                int val;

                if (!vec1.TryGetValue(x.Key, out val) && val < int.MaxValue)
                {
                    result[x.Key] = x.Value;
                }
            }

            return result;
        }

        public static SortedList<long, int> Merge(this SortedList<long, int> vec1, SortedList<long, int> vec2)
        {
            var result = new SortedList<long, int>();

            foreach (var x in vec1)
            {
                result[x.Key] = 1;
            }

            foreach (var x in vec2)
            {
                result[x.Key] = 1;
            }

            return result;
        }

        public static SortedList<long, int> Subtract(this SortedList<long, int> vec1, SortedList<long, int> vec2)
        {
            var result = new SortedList<long, int>();

            foreach (var x in vec1)
            {
                int val;

                if (vec2.TryGetValue(x.Key, out val) && val > 0)
                {
                    result[x.Key] = (val - 1);
                }
            }

            return result;
        }

        public static SortedList<long, int> ToVector(this string word, int offset, int length)
        {
            var vec = new SortedList<long, int>();

            foreach (var c in word.AsSpan(offset, length))
            {
                var codePoint = (int)c;

                if (vec.ContainsKey(codePoint))
                {
                    if (vec[codePoint] < int.MaxValue) vec[codePoint] += 1;
                }
                else
                {
                    vec[codePoint] = 1;
                }
            }

            return vec;
        }

        public static SortedList<long, int> ToVector(this string word)
        {
            var vec = new SortedList<long, int>();

            foreach (var c in word.ToCharArray())
            {
                var codePoint = (int)c;

                if (vec.ContainsKey(codePoint))
                {
                    if (vec[codePoint] < int.MaxValue) vec[codePoint] += 1;
                }
                else
                {
                    vec[codePoint] = 1;
                }
            }

            return vec;
        }

        public static float Magnitude(this SortedList<long, int> vector)
        {
            return (float) Math.Sqrt(DotSelf(vector));
        }

        public static SortedList<long, int> CreateDocumentVector(
            IEnumerable<SortedList<long, int>> termVectors, 
            (float identicalAngle, float foldAngle) similarity,
            NodeReader reader, 
            ITokenizer tokenizer)
        {
            var docVec = new SortedList<long, int>();

            foreach (var term in termVectors)
            {
                var hit = reader.ClosestMatch(term, similarity);
                var offset = hit.Node.PostingsOffsets != null ? hit.Node.PostingsOffsets[0] : hit.Node.PostingsOffset;

                if (hit.Score == 0 || offset < 0)
                {
                    throw new DataMisalignedException();
                }

                var termId = offset;

                if (docVec.ContainsKey(termId))
                {
                    if (docVec[termId] < int.MaxValue) docVec[termId] += 1;
                }
                else
                {
                    docVec.Add(termId, 1);
                }
            }

            return docVec;
        }

        public static bool ContainsMany(this string text, char c)
        {
            var vector = text.ToVector();

            if (vector[c] > 1)
            {
                return true;
            }

            return false;
        }

        public static int[] ToArray(this SortedList<long, int> vector)
        {
            var result = new int[vector.Count];
            var index = 0;

            foreach(var key in vector.Keys)
            {
                result[index++] = vector[key];
            }

            return result;
        }
    }
}
