using System;
using Unity.Entities;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A node set is a set of instantiated user nodes connected together in
    /// some particular way, although not necessarily completely connected.
    /// Nodes can communicate through flowing data or messages, and the
    /// execution pattern is defined from the connections you establish.
    /// <seealso cref="NodeDefinition{TNodeData}"/>
    /// <seealso cref="Create{TDefinition}"/>
    /// <seealso cref="Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID, ConnectionType)"/>
    /// <seealso cref="Update"/>
    /// </summary>
    public partial class NodeSet : NodeSetAPI, IDisposable
    {
        /// <summary>
        /// Construct a node set. Remember to dispose it.
        /// <seealso cref="Dispose"/>
        /// </summary>
        public NodeSet()
            : base(null, InternalDispatch.Tag)
        {
        }

        /// <summary>
        /// Initializes this <see cref="NodeSet"/> in a mode that's compatible with running together with ECS,
        /// through the use of <see cref="ComponentNode"/>s.
        /// The <paramref name="hostSystem"/> and this instance are tied together from this point, and you must
        /// update this set using the <see cref="Update(JobHandle)"/> function.
        /// See also <seealso cref="NodeSet()"/>.
        /// </summary>
        /// <remarks>
        /// Any instantiated nodes with <see cref="IKernelPortDefinition"/>s containing ECS types will be added
        /// as dependencies to <paramref name="hostSystem"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown if the <paramref name="hostSystem"/> is null
        /// </exception>
        public NodeSet(ComponentSystemBase hostSystem)
            : base(hostSystem, ComponentSystemDispatch.Tag)
        {

        }

        /// <summary>
        /// Query whether <see cref="Dispose"/> has been called on the <see cref="NodeSet"/>.
        /// </summary>
        public bool IsCreated => InternalIsCreated;

        /// <summary>
        /// Cleans up the node set, and releasing any resources associated with it.
        /// </summary>
        /// <remarks>
        /// It's expected that the node set is completely cleaned up, i.e. no nodes
        /// exist in the set, together with any <see cref="GraphValue{T}"/>.
        /// </remarks>
        public void Dispose() => InternalDispose();

        /// <summary>
        /// Updates the node set in two phases:
        ///
        /// 1. A message phase (simulation) where nodes are updated and messages
        /// are passed around
        /// 2. Aligning the simulation world and the rendering world and initiate
        /// the rendering.
        ///
        /// <seealso cref="RenderExecutionModel"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Can be thrown if invalid or missing dependencies were added through
        /// <see cref="InjectDependencyFromConsumer(JobHandle)"/>.
        ///
        /// Can also be thrown if this <see cref="NodeSet"/> was created with the ECS constructor
        /// <see cref="NodeSet(ComponentSystemBase)"/>, in which case you need to use the
        /// <see cref="Update(JobHandle)"/> function instead.
        /// </exception>
        public void Update()
        {
            if (HostSystem != null)
                throw new InvalidOperationException($"This {typeof(NodeSet)} was created together with a job component system, you must use the update function with an input {nameof(JobHandle)} argument");

            UpdateInternal(inputDependencies: default);
        }

        /// <summary>
        /// Overload of <see cref="Update()"/>. Use this function inside a <see cref="ComponentSystemBase"/>.
        /// </summary>
        /// <remarks>
        /// This function is only compatible if you used the <see cref="NodeSet(ComponentSystemBase)"/> constructor.
        /// </remarks>
        /// <param name="inputDeps">
        /// Input dependencies derived from <see cref="JobComponentSystem.OnUpdate(JobHandle)"/> or <see cref="SystemBase.Dependency"/>, pass the
        /// input dependencies into this function.
        /// </param>
        /// <returns>
        /// A <see cref="JobHandle"/> that should be returned or included in a dependency chain inside
        /// <see cref="JobComponentSystem.OnUpdate(JobHandle)"/> or assigned to <see cref="SystemBase.Dependency"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Can be thrown if this <see cref="NodeSet"/> was created without using the ECS constructor
        /// <see cref="NodeSet(ComponentSystemBase)"/>, in which case you need to use the
        /// <see cref="Update()"/> function instead.
        /// See also base documentation for <see cref="Update"/>
        /// </exception>
        public JobHandle Update(JobHandle inputDeps)
            => Update(inputDeps, ComponentSystemDispatch.Tag);

        /// <summary>
        /// Looks up the node definition for this handle.
        /// <seealso cref="NodeDefinition"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node handle does not refer to a valid instance.
        /// </exception>
        public new NodeDefinition GetDefinition(NodeHandle handle)
            => base.GetDefinition(handle);

        /// <summary>
        /// Looks up the specified node definition, creating it if it
        /// doesn't exist already.
        /// </summary>
        /// <exception cref="InvalidNodeDefinitionException">
        /// Thrown if the <typeparamref name="TDefinition"/> is not a valid
        /// node definition.
        /// </exception>
        public new TDefinition GetDefinition<TDefinition>()
            where TDefinition : NodeDefinition, new()
                => base.GetDefinition<TDefinition>();

        /// <summary>
        /// Looks up the verified node definition for this handle.
        /// <seealso cref="NodeDefinition"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node handle does not refer to a valid instance.
        /// </exception>
        public new TDefinition GetDefinition<TDefinition>(NodeHandle<TDefinition> handle)
            where TDefinition : NodeDefinition, new()
                => base.GetDefinition(handle);

        /// <summary>
        /// Injects external dependencies into this node set, so the next <see cref="Update()"/>
        /// synchronizes against consumers of any data from this node set.
        /// </summary>
        /// <seealso cref="GetGraphValueResolver(out JobHandle)"/>
        public void InjectDependencyFromConsumer(JobHandle handle)
            => InjectDependencyFromConsumerInternal(handle);

        /// <summary>
        /// Returns a <see cref="GraphValueResolver"/> that can be used to asynchronously
        /// read back graph state and buffers in a job. Put the resolver on a job ("consumer"),
        /// and schedule it against the parameter <paramref name="resultDependency"/>.
        ///
        /// Any job handles referencing the resolver must to be submitted back to the node
        /// set through <see cref="InjectDependencyFromConsumer(JobHandle)"/>.
        ///
        /// </summary>
        /// <param name="resultDependency">
        /// Contains an aggregation of dependencies from the last <see cref="Update()"/>
        /// for any created graph values.
        /// </param>
        /// <remarks>
        /// The returned resolver is only valid until the next <see cref="Update()"/> is
        /// issued, so call this function after every <see cref="Update()"/>.
        ///
        /// The resolver does not need to be cleaned up from the user's side.
        /// </remarks>
        public GraphValueResolver GetGraphValueResolver(out JobHandle resultDependency)
            => GetGraphValueResolverInternal(out resultDependency);

        public TDSLHandler GetDSLHandler<TDSLHandler>()
            where TDSLHandler : class, IDSLHandler, new()
                => (TDSLHandler)GetDSLHandler(DSLTypeMap.RegisterDSL<TDSLHandler>());
    }
}
