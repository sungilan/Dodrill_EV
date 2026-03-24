using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace petabytes.LTCLight
{
    public class GCContainer<T>
    {
        private List<T>[] buffers_;
        private int currBufferIdx = 0;
        private List<RangeInt> unusedRanges_;
        private Dictionary<int, int> indexRemap_;

        public delegate void OnMemMove(Dictionary<int, int> indexRemap);
        private OnMemMove memMoveOp_;
        
        public int Count => currentBuffer_.Count;
        public int Capacity => currentBuffer_.Capacity;

        private List<T> currentBuffer_
        {
            get => buffers_[currBufferIdx];
        }
        
        public T this[int i]
        {
            get => currentBuffer_[i];
            set => currentBuffer_[i] = value;
        }

        public void Clear()
        {
            currentBuffer_.Clear();
        }

        public GCContainer(int capacity, OnMemMove memMveOp)
        {
            buffers_ = new List<T>[2];
            for (int i = 0; i < buffers_.Length; ++i)
            {
                buffers_[i] = new List<T>(capacity);
            }
            memMoveOp_ = memMveOp;
            indexRemap_ = new Dictionary<int, int>(capacity);
            unusedRanges_ = new List<RangeInt>(capacity);
        }

        public void AddList(List<T> list, out int vertStart, out int vertEnd)
        {
            // Enlarge if no enough space
            if (currentBuffer_.Count + list.Count > currentBuffer_.Capacity)
            {
                CompactOrEnlarge(list.Count);
            }

            Debug.Assert(currentBuffer_.Count + list.Count <= currentBuffer_.Capacity);
            vertStart = currentBuffer_.Count;
            currentBuffer_.AddRange(list);
            vertEnd = currentBuffer_.Count;
        }

        public void AddList(List<T> list, out RangeInt range)
        {
            AddList(list, out range.start, out int end);
            range.length = end - range.start;
        }

        public void Add(T obj)
        {
            if (currentBuffer_.Count >= currentBuffer_.Capacity)
            {
                CompactOrEnlarge();
            }
            buffers_[currBufferIdx].Add(obj);
        }

        // Marked ranges must not overlay with each other.
        public void MarkRangeUnused(int start, int end)
        {
            if (end <= start) return;
            unusedRanges_.Add(new RangeInt(start, end - start));
        }

        private void Enlarge(int extra)
        {
            for (int i = 0; i < buffers_.Length; ++i)
            {
                // Enlarge 2 times by default 
                if (extra == -1)
                {
                    buffers_[i].Capacity *= 2;
                }
                else
                {
                    int sizeNeeded = buffers_[i].Count + extra;
                    if (sizeNeeded > buffers_[i].Capacity)
                    {
                        buffers_[i].Capacity = 2 * sizeNeeded;
                    }
                }
            }
        }

        // Compact buffer space, and enlarge if needed.
        public void CompactOrEnlarge(int extra = -1)
        {
            // If there is no unused range, enlarge buffer.
            if (unusedRanges_.Count == 0)
            {
                Enlarge(extra);
                return;
            }

            // Compact buffer.
            int nextBufferIdx = (currBufferIdx + 1) & 0x1;
            var nextBuffer = buffers_[nextBufferIdx];
            unusedRanges_.Sort((l, r) => l.start - r.start);
            nextBuffer.Clear();
            indexRemap_.Clear();

            int copyIdx = 0;
            for (int i = 0; i < unusedRanges_.Count; ++i)
            {
                var unusedRange = unusedRanges_[i];
                for (; copyIdx < unusedRange.start; ++copyIdx)
                {
                    nextBuffer.Add(currentBuffer_[copyIdx]);
                    indexRemap_[copyIdx] = nextBuffer.Count - 1;
                }

                // The removed range will be mapped to end point
                for (int j = unusedRange.start; j < unusedRange.end; ++j)
                {
                    indexRemap_[j] = nextBuffer.Count;
                }
                copyIdx = unusedRange.end;
            }

            // Copy remaining elements.
            for (; copyIdx < currentBuffer_.Count; ++copyIdx)
            {
                nextBuffer.Add(currentBuffer_[copyIdx]);
                indexRemap_[copyIdx] = nextBuffer.Count - 1;
            }
            indexRemap_[currentBuffer_.Count] = nextBuffer.Count;

            // Inform user we have compacted
            memMoveOp_(indexRemap_);

            unusedRanges_.Clear();
            currentBuffer_.Clear();
            currBufferIdx = nextBufferIdx;
            
            // Still not enough after compact
            if  (currentBuffer_.Count + extra > currentBuffer_.Capacity)
                Enlarge(extra);
        }
    }
}