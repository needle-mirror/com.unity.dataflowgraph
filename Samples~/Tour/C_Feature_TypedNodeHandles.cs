using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class C_Feature_TypedNodeHandles : MonoBehaviour
    {
        class MyNode : SimulationNodeDefinition<MyNode.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition { }
        }

        class MyOtherNode : SimulationNodeDefinition<MyOtherNode.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition { }
        }

        void Start()
        {
            using (var set = new NodeSet())
            {
                /*
                 * Here we are creating some nodes, as usual.
                 * Notice that the node handle is actually templated on your particular type of node. This enables static
                 * type checking of the graphs you create: You will get compiler errors for making invalid connections, 
                 * for instance. It also allows the node set to bypass most runtime checks on APIs, so it runs faster.
                 * We will look at this later.
                 */
                NodeHandle<MyNode> original = set.Create<MyNode>();

                /*
                 * In many cases however, you need to generically work with nodes with no idea about what they are.
                 * The node set contains a "weak API" as well, where you can work with type and port descriptions at 
                 * runtime instead.
                 * For this reason, there can also be an untyped or "weak" node handle. A "strong" node handle will 
                 * automatically decay to a weak one.
                 */
                NodeHandle untyped = original;

                /*
                 * To go back, you need to cast the handle. This will throw an exception if it isn't an exact match.
                 * There are also "softer" APIs like Is() and As().
                 */
                NodeHandle<MyNode> typed = set.CastHandle<MyNode>(untyped);

                if (!set.Is<MyOtherNode>(untyped))
                    Debug.Log("Untyped node handle originating from a MyNode type isn't a MyOtherNode");

                var nullableHandle = set.As<MyOtherNode>(untyped);

                if(nullableHandle == null || !nullableHandle.HasValue)
                    Debug.Log("Untyped node handle originating from a MyNode type isn't a MyOtherNode");

                set.Destroy(original);
            }
        }
    }
}
