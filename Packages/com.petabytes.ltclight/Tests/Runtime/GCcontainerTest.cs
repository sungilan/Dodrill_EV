using System.Collections;
using System.Collections.Generic;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityEngine;

namespace petabytes.LTCLight.Tests
{
    [TestFixture]
    public class GCcontainerTest
    {
        bool remapCalled = false;        

        struct MyRange
        {
            public int start;
            public int end;
        }
        private List<MyRange> ranges;
        
        private void onRemap(Dictionary<int, int> remap)
        {
            for (int i = 0; i < ranges.Count; ++i)
            {
                MyRange range = ranges[i];
                range.start = remap[range.start];
                range.end = remap[range.end];
                ranges[i] = range;
            }
            remapCalled = true;
        }

        [SetUp]
        public void SetUp()
        {
           ranges = new List<MyRange>();
           remapCalled = false;
        }
        
        [Test]
        public void TestContainerBasic()
        {
            GCContainer<Vector4> container = new GCContainer<Vector4>(2, remap => { });
            for (int i = 0; i < 10; ++i)
            {
                container.Add(new Vector4(i, 0, 0, 0));
            }
            for (int i = 0; i < 10; ++i)
            {
                Assert.AreEqual(container[i], new Vector4(i, 0, 0, 0));
            }
            Assert.AreEqual(container.Count, 10);
            container.Clear();
            Assert.AreEqual(container.Count, 0);
            container.Add(new  Vector4(10, 10, 10, 10));
            container.Add(new  Vector4(10, 10, 10, 10));
            container[0] = new  Vector4(10, 10, 10, 11);
            
            Assert.AreEqual(container[0], new Vector4(10, 10, 10, 11));
            Assert.AreEqual(container[1], new Vector4(10, 10, 10, 10));
        }

        [Test]
        public void TestContainerMarkPartialUnused()
        {
            ranges.Add(new MyRange() { start = 0, end = 10, });
            ranges.Add(new MyRange() { start = 10, end = 20, });
            GCContainer<int> container = new GCContainer<int>(20, onRemap);
            
            for (int i = 0; i < 20; ++i)
            {
                container.Add(i);
            }
            
            container.MarkRangeUnused(5, 15);
            container.CompactOrEnlarge();
            
            Assert.AreEqual(10, container.Count);
            Assert.AreEqual(0, ranges[0].start);
            Assert.AreEqual(5, ranges[0].end);
            
            Assert.AreEqual(5, ranges[1].start);
            Assert.AreEqual(10, ranges[1].end);

            for (int i = 0; i < 5; ++i)
            {
                Assert.AreEqual(container[i], i);
            }
            
            for (int i = 5; i < 10; ++i)
            {
                Assert.AreEqual(container[i], i+10);
            }
        }
        
        [Test]
        public void TestContainerGC()
        {
            ranges.Add(new MyRange() { start = 0, end = 10, });
            ranges.Add(new MyRange() { start = 10, end = 25, });
            ranges.Add(new MyRange() { start = 25, end = 30, });
            ranges.Add(new MyRange() { start = 30, end = 40, });

            GCContainer<int> container = new GCContainer<int>(20, onRemap);
            
            Assert.AreEqual(container.Capacity, 20);
            
            for (int i = 0; i < 40; ++i)
            {
                container.Add(i);
                if (i == 20)
                {
                    Assert.AreEqual(container.Capacity, 40);
                }
            }
            Assert.False(remapCalled);
            
            container.MarkRangeUnused(ranges[2].start, ranges[2].end);
            ranges.RemoveAt(2);
            
            container.Add(41);
            Assert.True(remapCalled);
            Assert.AreEqual(container.Count, 36);
            Assert.AreEqual(container.Capacity, 40);
            
            Assert.AreEqual(ranges[0].start, 0);
            Assert.AreEqual(ranges[0].end, 10);
            Assert.AreEqual(ranges[1].start, 10);
            Assert.AreEqual(ranges[1].end, 25);
            Assert.AreEqual(ranges[2].start, 25);
            Assert.AreEqual(ranges[2].end, 35);

            for (int i = ranges[0].start; i < ranges[0].end; i++)
            {
                Assert.AreEqual(container[i], i);
            }
            
            for (int i = ranges[1].start; i < ranges[1].end; i++)
            {
                Assert.AreEqual(container[i], i);
            }
            
            for (int i = ranges[2].start; i < ranges[2].end; i++)
            {
                Assert.AreEqual(container[i], i+5);
            }
            
            Assert.AreEqual(container[container.Count - 1], 41);
        }

        [Test]
        public void TestContainerGCWhileAdding()
        {
            GCContainer<int> container = new GCContainer<int>(10, onRemap);
            ranges.Add(new MyRange() {start = 0, end = 5});
            for (int i = 0; i < 5; ++i)
            {
                container.Add(i);
            }
            Assert.AreEqual(5, container.Count);
            Assert.AreEqual(10, container.Capacity);
            
            container.MarkRangeUnused(ranges[0].start, ranges[0].end);
            container.AddList(new List<int>() {11, 12, 13, 14, 15, 16, 17, 18, 19, 20}, out var range);
            Assert.AreEqual(10, container.Count);
            Assert.AreEqual(10, container.Capacity);
            Assert.AreEqual(0, ranges[0].start);
            Assert.AreEqual(0, ranges[0].end);
            
            Assert.AreEqual(0, range.start);
            Assert.AreEqual(10, range.end);
            ranges[0] = new MyRange() { start = range.start, end = range.end }; 
            
            container.MarkRangeUnused(5, 10);
            container.AddList(new List<int>() {21,22,23,24,25,26,27,28,29,30}, out range);
            Assert.AreEqual(15, container.Count);
            Assert.AreEqual(30, container.Capacity);
            Assert.AreEqual(0, ranges[0].start);
            Assert.AreEqual(5, ranges[0].end);
            Assert.AreEqual(5, range.start);
            Assert.AreEqual(15, range.end);
        }
    }
}
