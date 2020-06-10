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
        class NodeWithManagedData : NodeDefinition<NodeWithManagedData.Data, EmptyPorts>
        {
            [Managed]
            public struct Data : INodeData
            {
                public MObject Object;
            }
        }

        [UnityTest]
        public IEnumerator NodeDefinition_DeclaredManaged_CanRetainAndRelease_ManagedObjects()
        {
            const float k_Time = 5;

            using (var set = new NodeSet())
            {
                var handle = set.Create<NodeWithManagedData>();

                Assert.Zero(MObject.Instances);
                set.GetNodeData<NodeWithManagedData.Data>(handle).Object = new MObject();
                Assert.AreEqual(1, MObject.Instances);

                var currentTime = Time.realtimeSinceStartup;

                while (Time.realtimeSinceStartup - currentTime < k_Time)
                {
                    GC.Collect();
                    Assert.AreEqual(1, MObject.Instances);
                    set.Update();
                    yield return null;
                }

                Assert.AreEqual(1, MObject.Instances);
                set.GetNodeData<NodeWithManagedData.Data>(handle).Object = null;

                set.Update();
                yield return ManagedMemoryAllocatorTests.AssertManagedObjectsReleasedInTime();

                set.Destroy(handle);
            }
        }
    }
}
