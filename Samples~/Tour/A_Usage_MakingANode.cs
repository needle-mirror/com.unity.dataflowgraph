using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class A_Usage_MakingANode : MonoBehaviour
    {
        /*
         * Creating a node for a graph always starts out by deriving a class from NodeDefinition.
         * There are a couple of overloaded versions, depending on the composition of your node and what sort of 
         * features you're going to be using.
         * Here we will be using the simplest version.
         * 
         * A node definition can be thought of as a static, "class" declaration of your node's capabilities,
         * requirements and features. There's only one node definition instance per set, no matter how many nodes 
         * you're creating. 
         * 
         * The node definition always defines a "data instance description", a structure implementing INodeData.
         * This particular structure is what is actually being instantiated, when you create a node from a node definition.
         * We'll look at this in more detail in "B_Usage_HavingInstanceData.cs"
         * 
         * The function of the node definition is otherwise custom event handling (construction, destruction, messages)
         * and static descriptions of your node's I/O contract: What messages it can receive and output, what DSLs it works
         * with and what data it will process.
         * 
         */
        class MyNode : NodeDefinition<MyNode.MyInstanceData>
        {
            /*
             * This is our instance data. Ignore this for now.
             */
            public struct MyInstanceData : INodeData { }

            /*
             * Node definitions only exist once per set, so it might be useful
             * to do some one-time initialization here.
             */
            public MyNode()
            {
                Debug.Log("My node's definition just got created");
            }

            /*
             * A node definition can also provide an interface to the user.
             */
            public void Hello()
            {
                Debug.Log("Hello, world!");
            }

            /*
             * You can clean up shared resources in the Dispose() function.
             */
            public override void Dispose()
            {
                Debug.Log("My node's definition just got disposed");
            }
        }

        /*
         * Let's make a graph, and get a hold of our node definition!
         */
        void Start()
        {
            /*
             * Graphs are represented as a set of nodes (a NodeSet) containing all the required meta data for execution.
             * Remember to clean it up explicitly, done here with a "using" scope.
             */
            using (var set = new NodeSet())
            {
                /*
                 * Node definitions can be directly looked up by type,
                 * otherwise they are created lazily when creating nodes.
                 */
                var myNodeDefinition = set.GetFunctionality<MyNode>();
                myNodeDefinition.Hello();
            }
        }
    }
}
