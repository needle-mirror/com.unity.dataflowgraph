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
        class MyNode : SimulationNodeDefinition<MyNode.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition { }

            /// <summary>
            /// A counter to identify created nodes.
            /// </summary>
            static int NodeCounter;

            public MyNode() => Debug.Log("My node's definition just got created");

            /*
             * This is our per-node instance data. 
             * Note that the presence of this INodeData nested struct is automatically detected for you, therefore you
             * can keep it declared private.
             * Members we have here exist for every node.
             * You can access contents of the data in handlers as shown below.
             */
            struct MyInstanceData : INodeData, IInit, IDestroy
            {
                /// <summary>
                /// The number of this node.
                /// </summary>
                int NodeNumber;

                /*
                 * Implementing IInit.Init() allows you to do custom initialization for your node data,
                 * whenever a user creates a new node of your kind.
                 */
                public void Init(InitContext ctx)
                {
                    // Let's uniquely identify and store some data in this node:
                    var nodeNumber = ++NodeCounter;
                    NodeNumber = nodeNumber;
                    Debug.Log($"Created node number {nodeNumber}");
                }

                /*
                 * Similarly, we can do custom destruction for a node by implementing IDestroy.Destroy().
                 */
                public void Destroy(DestroyContext ctx)
                {
                    Debug.Log($"Destroyed node number: {NodeNumber}");
                }
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
