using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace petabytes.LTCLight.Tests
{
    [TestFixture]
    public class PrefilterTextureTest
    {
        PrefilterTexture prefilterTexture;
        Texture[] fakeTexs = new Texture[16];
        
        [SetUp]
        public void SetUp()
        {
           prefilterTexture = new PrefilterTexture();
           for (int i = 0; i < 16; i++)
           {
               fakeTexs[i] = new Texture2D(512, 512);
           }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < 16; i++)
            {
                Object.DestroyImmediate(fakeTexs[i]);
            }
        }
        
        [Test]
        public void TestRegisterTexture()
        {
            Assert.AreEqual(0, prefilterTexture.SlotUsageBit);
            // Register null texture
            Assert.False(prefilterTexture.AddTexture(null, false, out var region));
            
            // Register texture first time
            Assert.True(prefilterTexture.AddTexture(fakeTexs[0], false, out region));
            Assert.True(prefilterTexture.GetPrefilterInfo(fakeTexs[0], out var info));
            Assert.AreEqual(1, info.RefCount);
            Assert.AreEqual(false, info.IsRealTime);
            Assert.AreEqual(fakeTexs[0], info.Tex);
            Assert.AreEqual(0, info.SlotIdx);
            Assert.AreEqual(new RectInt(0, 0, 512, 512), info.Region);
            Assert.AreEqual(true, info.needsPrefilter);
            
            Assert.AreEqual(1, prefilterTexture.SlotUsageBit);
            Assert.AreEqual(new RectInt(0, 0, 512, 512), region);

            // Re-register same texture
            Assert.True(prefilterTexture.AddTexture(fakeTexs[0], false, out region));
            Assert.True(prefilterTexture.GetPrefilterInfo(fakeTexs[0], out info));
            Assert.AreEqual(2, info.RefCount);
            
            for (int i = 0; i < 16; i++)
            {
                Assert.True(prefilterTexture.AddTexture(fakeTexs[i], false, out region));
                Assert.AreEqual(new RectInt(i%4*512, i/4*512, 512, 512), region);
                Assert.True(prefilterTexture.GetPrefilterInfo(fakeTexs[i], out info));
                Assert.AreEqual(i, info.SlotIdx);
            }
            
            Assert.AreEqual((UInt16)0xFFFF, (UInt16)prefilterTexture.SlotUsageBit);
        }

        [Test]
        public void TestUnregisterTexture()
        {
            for (int i = 0; i < 16; i++)
            {
                Assert.True(prefilterTexture.AddTexture(fakeTexs[i], false, out _));
            }
            
            prefilterTexture.RemoveTexture(fakeTexs[7]);
            Assert.AreEqual((UInt16)0xFF7F, (ushort)prefilterTexture.SlotUsageBit);
            
            prefilterTexture.RemoveTexture(fakeTexs[3]);
            Assert.AreEqual((UInt16)0xFF77, (ushort)prefilterTexture.SlotUsageBit);
            
            // Reuse fakeTexs[3] slot
            Assert.True(prefilterTexture.AddTexture(fakeTexs[7], false, out var rect));
            Assert.AreEqual(rect, new RectInt(512*3, 0, 512, 512));
            Assert.AreEqual((UInt16)0xFF7F, (ushort)prefilterTexture.SlotUsageBit);
        }

        [Test]
        public void TestRefCount()
        {
            // Register twice
            Assert.True(prefilterTexture.AddTexture(fakeTexs[0], false, out _));
            Assert.True(prefilterTexture.AddTexture(fakeTexs[0], false, out _));
            
            Assert.True(prefilterTexture.GetPrefilterInfo(fakeTexs[0], out var info));
            Assert.AreEqual(2, info.RefCount);
            Assert.AreEqual((ushort)0x1,  (ushort)prefilterTexture.SlotUsageBit);
            
            prefilterTexture.RemoveTexture(fakeTexs[1]);
            Assert.True(prefilterTexture.GetPrefilterInfo(fakeTexs[0], out info));
            Assert.AreEqual(2, info.RefCount);
            Assert.AreEqual((ushort)0x1,  (ushort)prefilterTexture.SlotUsageBit);
            
            prefilterTexture.RemoveTexture(fakeTexs[0]);
            Assert.True(prefilterTexture.GetPrefilterInfo(fakeTexs[0], out info));
            Assert.AreEqual(1, info.RefCount);
            Assert.AreEqual((ushort)0x1,  (ushort)prefilterTexture.SlotUsageBit);
            
            prefilterTexture.RemoveTexture(fakeTexs[0]);
            Assert.False(prefilterTexture.GetPrefilterInfo(fakeTexs[0], out info));
            Assert.AreEqual((ushort)0x0,  (ushort)prefilterTexture.SlotUsageBit);
        }
    }
}
