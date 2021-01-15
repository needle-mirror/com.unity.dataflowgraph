using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    using MObject = ManagedMemoryAllocatorTests.ManagedObject;

    public class RuntimeTests
    {
        class NodeWithManagedData : SimulationNodeDefinition<NodeWithManagedData.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            [Managed]
            public struct Data : INodeData, IInit, IDestroy
            {
                public MObject Object;

                public void Init(InitContext ctx)
                {
                    Object = new MObject();
                }

                public void Destroy(DestroyContext context)
                {
                    Object = null;
                }
            }
        }

        [Test]
        public void NodeDefinition_DeclaredManaged_CanRetainAndRelease_ManagedObjects()
        {
            const float k_Loops = 5;

            Assert.Zero(MObject.Instances);

            using (var set = new NodeSet())
            {
                ManagedMemoryAllocatorTests.AssertManagedObjectsReleased(() =>
                {
                    var handle = set.Create<NodeWithManagedData>();

                    Assert.AreEqual(1, MObject.Instances);

                    for (var i = 0; i < k_Loops; ++i)
                    {
                        ManagedMemoryAllocatorTests.RequestGarbageCollection();
                        Assert.AreEqual(1, MObject.Instances);
                        set.Update();
                    }

                    Assert.AreEqual(1, MObject.Instances);

                    set.Destroy(handle);

                    set.Update();
                });
            }
        }
    }
}
