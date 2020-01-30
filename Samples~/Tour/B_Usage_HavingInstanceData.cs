using System.Collections.Generic;
using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class B_Usage_HavingInstanceData : MonoBehaviour
    {
        /*
         * The node definition can define a "data instance description", a structure implementing INodeData.
         * This particular structure is what is actually being instantiated, when you create a node from a node definition.
         */
        class MyNode : NodeDefinition<MyNode.MyInstanceData, MyNode.MyPorts>
        {
            /*
             * This is our per-node instance data. 
             * Members we have here exist for every node.
             * You can retrieve the contents of the data using a node handle, as shown below.
             */
            public struct MyInstanceData : INodeData
            {
                /// <summary>
                /// The number of this node.
                /// </summary>
                public int NodeNumber;
            }

            public struct MyPorts : ISimulationPortDefinition { }

            /// <summary>
            /// A counter to identify created nodes.
            /// </summary>
            public int NodeCounter;

            public MyNode() => Debug.Log("My node's definition just got created");

            /*
             * Overriding Init() allows you to do custom initialization for your node data,
             * whenever a user creates a new node of your kind.
             */
            protected override void Init(InitContext ctx)
            {
                /*
                 * You can retrieve the contents of the node that was just created using GetNodeData + the initialization 
                 * context's .Handle.
                 * 
                 * Notice the usage of "ref". GetNodeData returns a reference to the node data, so you can modify it.
                 */
                ref var myData = ref GetNodeData(ctx.Handle);

                // Let's uniquely identify and store some data in this node:
                var nodeNumber = ++NodeCounter;
                myData.NodeNumber = nodeNumber;
                Debug.Log($"Created node number {nodeNumber}");
            }

            /*
             * Similarly, we can do custom destruction for a node by overriding Destroy().
             */
            protected override void Destroy(NodeHandle handle)
            {
                var data = GetNodeData(handle);
                Debug.Log($"Destroyed node number: {data.NodeNumber}");
            }

            protected override void Dispose() => Debug.Log("My node's definition just got disposed");
        }

        List<NodeHandle> m_NodeList = new List<NodeHandle>();
        NodeSet m_Set;

        void OnEnable()
        {
            m_Set = new NodeSet();    
        }

        void OnGUI()
        {
            if(GUI.Button(new Rect(50, 50, 100, 20), "Create a node!"))
            {
                /* 
                 * Using Create<NodeType>() on a node set creates a new node of that type inside the host node set.  
                 * As mentioned before -here, our node definition is being created automatically the moment we create
                 * a node from them.
                 */
                var node = m_Set.Create<MyNode>();
                m_NodeList.Add(node);
            }
        }

        void OnDisable()
        {
            /*
             * Nodes in the data flow graph always needs to be destroyed, otherwise it is considered a leak.
             * So remember to keep track of them.
             */
            m_NodeList.ForEach(n => m_Set.Destroy(n));
            m_NodeList.Clear();
            m_Set.Dispose();
        }

    }
}
