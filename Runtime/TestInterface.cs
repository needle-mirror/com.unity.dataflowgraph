using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A unique node testing context provided to test functions (<see cref="SimulationTestFunctionWithContext{TNodeData}"/>)
    /// that are injected into simulation via <see cref="NodeSet.SendTest{TNodeData}(Unity.DataFlowGraph.NodeHandle,SimulationTestFunctionWithContext{TNodeData})"/>.
    /// Allows recovery of the tested node's simulation data in order for the function to make assertions about its
    /// contents and throw exceptions (such as those from an <see cref="NUnit.Framework.Assert"/>).
    /// </summary>
    public ref struct SimulationTestContext<TNodeData>
        where TNodeData : struct, INodeData
    {
        NodeSetAPI m_Set;
        NodeHandle m_Handle;

        /// <summary>
        /// The <see cref="INodeData"/> instance associated with the node currently being tested.
        /// </summary>
        public TNodeData NodeData => m_Set.GetNodeData<TNodeData>(m_Handle);

        /// <summary>
        /// Allows testing child nodes of this node. Child nodes are exclusively direct descendants that were created by
        /// the node associated with this <see cref="SimulationTestContext{TNodeData}"/>. Specifying any other node handle is undefined behaviour.
        /// <seealso cref="NodeSet.SendTest{TNodeData}"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given <typeparamref name="TSubNodeData"/> type does not correspond to that declared in the node's <see cref="NodeDefinition"/>
        /// </exception>
        public void SendTest<TSubNodeData>(NodeHandle handle, SimulationTestFunctionWithContext<TSubNodeData> code)
            where TSubNodeData : struct, INodeData
                => m_Set.SendTestInternal(handle, code);

        /// <summary>
        /// Shorthand version of <see cref="SendTest{TSubNodeData}(Unity.DataFlowGraph.NodeHandle,SimulationTestFunctionWithContext{TSubNodeData})"/>
        /// omitting the <see cref="SimulationTestContext{TNodeData}"/> for simple test cases which do not require testing of child nodes.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given <typeparamref name="TSubNodeData"/> type does not correspond to that declared in the node's <see cref="NodeDefinition"/>
        /// </exception>
        public void SendTest<TSubNodeData>(NodeHandle handle, SimulationTestFunction<TSubNodeData> code)
            where TSubNodeData : struct, INodeData
                => m_Set.SendTestInternal<TSubNodeData>(handle, ctx => code(ctx.NodeData));

        /// <summary>
        /// Tests whether the supplied node handle refers to a currently valid node instance.
        /// <seealso cref="NodeSet.Exists"/>
        /// </summary>
        public bool Exists(NodeHandle handle)
            => m_Set.Exists(handle);

        internal SimulationTestContext(NodeSetAPI set, NodeHandle handle)
        {
            // Check if the node is valid and data type is correct up front.
            set.CheckNodeDataType<TNodeData>(handle);
            m_Set = set;
            m_Handle = handle;
        }
    }

    /// <summary>
    /// A function used for testing the internal simulation data of a node.
    /// </summary>
    public delegate void SimulationTestFunction<TNodeData>(TNodeData data) where TNodeData : struct, INodeData;

    /// <summary>
    /// A function used for testing the internal simulation data of a node with an associated context allowing further testing
    /// of child nodes.
    /// </summary>
    public delegate void SimulationTestFunctionWithContext<TNodeData>(SimulationTestContext<TNodeData> ctx) where TNodeData : struct, INodeData;

    public partial class NodeSetAPI
    {
        List<ExceptionDispatchInfo> m_CollectedTestExceptions = new List<ExceptionDispatchInfo>(), m_TestExceptions = new List<ExceptionDispatchInfo>();

        internal void SendTestInternal<TNodeData>(NodeHandle handle, SimulationTestFunctionWithContext<TNodeData> code)
            where TNodeData : struct, INodeData
        {
            var ctx = new SimulationTestContext<TNodeData>(this, handle);
            try
            {
                code(ctx);
            }
            catch(Exception e)
            {
                m_TestExceptions.Add(ExceptionDispatchInfo.Capture(e));
            }
        }

        void CollectTestExceptions()
        {
            if (m_TestExceptions.Count > 0)
            {
                m_CollectedTestExceptions.AddRange(m_TestExceptions);
                m_TestExceptions.Clear();
            }
        }

        internal void ThrowCollectedTestExceptionInternal()
        {
            if (m_CollectedTestExceptions.Count > 0)
            {
                var exception = m_CollectedTestExceptions[0];
                m_CollectedTestExceptions.RemoveAt(0);
                exception.Throw();
            }
        }

        void LogPendingTestExceptions()
        {
            CollectTestExceptions();
            if (m_CollectedTestExceptions.Count > 0)
            {
                Debug.LogError($"{m_CollectedTestExceptions.Count} pending test exception(s) remain uncollected. Use {nameof(NodeSet)}.{nameof(NodeSet.ThrowCollectedTestException)}() to collect them.");
                foreach (var exception in m_CollectedTestExceptions)
                    Debug.LogException(exception.SourceException);
                m_TestExceptions.Clear();
            }
        }
    }

    public partial class NodeSet
    {
        /// <summary>
        /// Sends a function into the simulation for testing purposes. The given function will be queued into the stream
        /// of messages destined for the given node and will be invoked after any messages queued before it. The function
        /// should be used to throw testing exceptions (such as those from an <see cref="NUnit.Framework.Assert"/>) which
        /// will be collected in the next <see cref="NodeSet.Update"/> and can subsequently be recovered by calling
        /// <see cref="NodeSet.ThrowCollectedTestException"/>. It is an error to <see cref="NodeSet.Dispose"/>
        /// with pending uncollected test exceptions.
        /// </summary>
        /// <remarks> This function is provided solely for test purposes and is not guaranteed to be available in release
        /// builds. This function should not be used to inject or recover any data from simulation or otherwise influence
        /// normal runtime behavior of nodes in non-testing scenarios.</remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given <typeparamref name="TNodeData"/> type does not correspond to that declared in the node's <see cref="NodeDefinition"/>
        /// </exception>
        public void SendTest<TNodeData>(NodeHandle handle, SimulationTestFunctionWithContext<TNodeData> code)
            where TNodeData : struct, INodeData
                => SendTestInternal(handle, code);

        /// <summary>
        /// Shorthand version of <see cref="SendTest{TNodeData}(Unity.DataFlowGraph.NodeHandle,SimulationTestFunctionWithContext{TNodeData})"/>
        /// omitting the <see cref="SimulationTestContext{TNodeData}"/> for simple test cases which do not require testing of child nodes.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given <typeparamref name="TNodeData"/> type does not correspond to that declared in the node's <see cref="NodeDefinition"/>
        /// </exception>
        public void SendTest<TNodeData>(NodeHandle handle, SimulationTestFunction<TNodeData> code)
            where TNodeData : struct, INodeData
                => SendTestInternal<TNodeData>(handle, ctx => code(ctx.NodeData));

        /// <summary>
        /// Will rethrow any test exception (see <see cref="SendTest{TNodeData}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.SimulationTestFunctionWithContext{TNodeData})"/>)
        /// that was collected in a previous <see cref="NodeSet.Update"/>. If there are multiple pending test exceptions
        /// each individual call to this method will dequeue and rethrow the oldest one.
        /// </summary>
        /// <remarks>It is not an error to call this method if there are no pending test exceptions to be thrown.</remarks>
        public void ThrowCollectedTestException()
            => ThrowCollectedTestExceptionInternal();
    }
}
