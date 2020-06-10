using System;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    public class AdapterLinkTests
    {
        class TestNode : NodeDefinition<EmptyPorts> { }

        [Test]
        public void AdaptHandle_DoesNotThrow_OnInvalidAndDestroyedNodes()
        {
            using (var set = new NodeSet())
            {
                Assert.DoesNotThrow(() => set.Adapt(new NodeHandle<TestNode>()));
                Assert.DoesNotThrow(() => set.Adapt(new NodeHandle()));

                var handle = set.Create<TestNode>();
                set.Destroy(handle);

                Assert.DoesNotThrow(() => set.Adapt<TestNode>(handle));
                Assert.DoesNotThrow(() => set.Adapt((NodeHandle)handle));

            }
        }

        [Test]
        public void AdaptTo_Throws_OnInvalidAndDestroyedNodes()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.Adapt(new NodeHandle<TestNode>()).To<int>());
                Assert.Throws<ArgumentException>(() => set.Adapt(new NodeHandle()).To<int>());

                var handle = set.Create<TestNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.Adapt<TestNode>(handle).To<int>());
                Assert.Throws<ArgumentException>(() => set.Adapt((NodeHandle)handle).To<int>());
            }
        }

        [Test]
        public void AdaptTo_Throws_OnInvalidConversion()
        {
            using (var set = new NodeSet())
            {
                var handle = set.Create<TestNode>();

                Assert.Throws<InvalidCastException>(() => set.Adapt<TestNode>(handle).To<int>());
                Assert.Throws<InvalidCastException>(() => set.Adapt((NodeHandle)handle).To<int>());

                set.Destroy(handle);

            }
        }
    }
}

