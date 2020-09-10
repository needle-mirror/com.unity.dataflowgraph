using System;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    public class AdapterLinkTests
    {
        [Test]
        public void AdaptHandle_DoesNotThrow_OnInvalidAndDestroyedNodes()
        {
            using (var set = new NodeSet())
            {
                Assert.DoesNotThrow(() => set.Adapt(new NodeHandle<EmptyNode>()));
                Assert.DoesNotThrow(() => set.Adapt(new NodeHandle()));

                var handle = set.Create<EmptyNode>();
                set.Destroy(handle);

                Assert.DoesNotThrow(() => set.Adapt<EmptyNode>(handle));
                Assert.DoesNotThrow(() => set.Adapt((NodeHandle)handle));

            }
        }

        [Test]
        public void AdaptTo_Throws_OnInvalidAndDestroyedNodes()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.Adapt(new NodeHandle<EmptyNode>()).To<int>());
                Assert.Throws<ArgumentException>(() => set.Adapt(new NodeHandle()).To<int>());

                var handle = set.Create<EmptyNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.Adapt<EmptyNode>(handle).To<int>());
                Assert.Throws<ArgumentException>(() => set.Adapt((NodeHandle)handle).To<int>());
            }
        }

        [Test]
        public void AdaptTo_Throws_OnInvalidConversion()
        {
            using (var set = new NodeSet())
            {
                var handle = set.Create<EmptyNode>();

                Assert.Throws<InvalidCastException>(() => set.Adapt<EmptyNode>(handle).To<int>());
                Assert.Throws<InvalidCastException>(() => set.Adapt((NodeHandle)handle).To<int>());

                set.Destroy(handle);

            }
        }
    }
}

