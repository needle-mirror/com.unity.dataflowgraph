using UnityEngine;

namespace Unity.DataFlowGraph.Tour
{
    public class F_Usage_Simulation : MonoBehaviour
    {
        /*
         * In this example, we'll explore what "simulation" means, and how to use it.
         * 
         * In a data flow graph, execution flow is split into two parts: simulation and rendering.
         * Simulation is event based, and it's purpose is to do everything needed to set the graph up for rendering.
         * This can include:
         * - Ingesting data / events from the game
         * - Handling updating of nodes
         * - Creating / destroying nodes and connections
         * - Flowing messages
         * - Updating "time" (usually)
         * 
         * Later on, we'll explore exactly what "rendering" means but for now, just think of it as the a processing counterpart
         * to the event-based processing happening in the simulation.
         * 
         * The simulation's purpose is, in large part, to prepare and rig the graph for fast execution in the rendering 
         * phase, where everything is immutable, except for the flowing data.
         * Thus, simulation can be viewed as progressing the setup of the graph between rendering invocations. In the same way, you
         * can simulate a data flow graph without actually rendering it. Or you can render it, without simulating it further.
         * 
         * Concretely in this example, we'll just explore updating a node and a set.
         */
        class MyNode : SimulationNodeDefinition<MyNode.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition { }

            struct NodeHandlers : INodeData, IUpdate, IInit
            {
                /*
                 * Implementing the IUpdate.Update function in an INodeData implementation in a nested declaration within
                 * the NodeDefinition will give you the ability to update each simulation update.
                 * 
                 * Normally nodes will just respond to incoming messages, otherwise.
                 */
                public void Update(UpdateContext ctx)
                {
                    Debug.Log("Updating MyNode");
                }

                /*
                 * In order to receive updates, we need to subscribe to them first.
                 * For nodes that always want to update, we will have to do this at initialization time.
                 * 
                 * Alternatively, you can start/stop updating in response to a message - this is useful for implementing 
                 * "coroutines" or nodes with waiting functionality.
                 */
                public void Init(InitContext ctx)
                {
                    /*
                     * Calling this function registers this node to be updated in subsequent simulation updates in a particular
                     * nodeset.
                     */
                    ctx.RegisterForUpdate();
                    /*
                     * If you want to stop receiving updates, just call this function.
                     */
                    ctx.RemoveFromUpdate();
                    /*
                     * In this case, we do want updates, though! Be careful to match these properly. You cannot remove yourself from
                     * updates if you never subscribed to them!
                     * 
                     * Note that these changes to update subscription take effect as of the *next* simulation update.
                     * 
                     * Therefore, you will not get an "Update" in the same simulation update in which you were created.
                     */
                    ctx.RegisterForUpdate();
                }
            }
        }

        NodeSet m_Set;
        NodeHandle<MyNode> m_Node;

        void OnEnable()
        {
            m_Set = new NodeSet();
            m_Node = m_Set.Create<MyNode>();
        }

        void Update()
        {
            m_Set.Update();
        }

        void OnDisable()
        {
            m_Set.Destroy(m_Node);
            m_Set.Dispose();
        }
    }
}
