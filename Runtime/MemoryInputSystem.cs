using System;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Base common interface for unique type tag used by <see cref="MemoryInputSystem{TTag, TBufferToMove}"/> and <see cref="NodeMemoryInput{TTag, TBufferToMove}"/>.
    /// </summary>
    [Obsolete("Use ComponentNode + connections instead")]
    public interface INodeMemoryInputTag { }

    /// <summary>
    /// Component for specifying a memory mapping from an entity to a node.
    /// </summary>
    /// <typeparam name="TTag">
    /// A unique type tag to associate memory mappings with a particular instantiation of
    /// <see cref="MemoryInputSystem{TTag, TBufferToMove}"/>.
    /// </typeparam>
    /// <typeparam name="TBufferToMove">
    /// The <see cref="IBufferElementData"/> type that this system should move.
    /// </typeparam>
    [Obsolete("Use ComponentNode + connections instead")]
    public struct NodeMemoryInput<TTag, TBufferToMove> : IComponentData
        where TTag : INodeMemoryInputTag
        where TBufferToMove : struct, IBufferElementData
    {
        /// <summary>
        /// Sets up a mapping of <see cref="TBufferToMove"/> to the given data input buffer port of the given node.
        /// </summary>
        public NodeMemoryInput(NodeHandle node, InputPortID port)
        {
            Node = node;
            InternalPort = new InputPortArrayID(port);
            ResolvedDestination = default;
            WasCreatedStrongly = false;
        }

        /// <summary>
        /// Overload of <see cref="NodeMemoryInput{TTag,TBufferToMove}(NodeHandle, InputPortID)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        public NodeMemoryInput(NodeHandle node, InputPortID portArray, int arrayIndex)
        {
            Node = node;
            InternalPort = new InputPortArrayID(portArray, arrayIndex);
            ResolvedDestination = default;
            WasCreatedStrongly = false;
        }

        /// <summary>
        /// See <see cref="NodeMemoryInput{TTag,TBufferToMove}(NodeHandle, InputPortID)"/>.
        /// </summary>
        public static NodeMemoryInput<TTag, TBufferToMove> Create<TDefinition>(NodeHandle<TDefinition> node, DataInput<TDefinition, Buffer<TBufferToMove>> port)
            where TDefinition : NodeDefinition, new()
        {
            return new NodeMemoryInput<TTag, TBufferToMove>
            {
                Node = node,
                InternalPort = new InputPortArrayID((InputPortID)port),
                ResolvedDestination = default,
                WasCreatedStrongly = true
            };
        }

        /// <summary>
        /// Overload of <see cref="NodeMemoryInput{TTag,TBufferToMove}(NodeHandle, InputPortID)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        public static NodeMemoryInput<TTag, TBufferToMove> Create<TDefinition>(NodeHandle<TDefinition> node, PortArray<DataInput<TDefinition, Buffer<TBufferToMove>>> portArray, int arrayIndex)
            where TDefinition : NodeDefinition, new()
        {
            return new NodeMemoryInput<TTag, TBufferToMove>
            {
                Node = node,
                InternalPort = new InputPortArrayID((InputPortID)portArray, arrayIndex),
                ResolvedDestination = default,
                WasCreatedStrongly = true
            };
        }

        internal InputPair ResolvedDestination;
        internal InputPortArrayID InternalPort;

        public NodeHandle Node { get; private set; }
        public InputPortID Port => InternalPort.PortID;
        public ushort ArrayIndex => InternalPort.ArrayIndex;
        internal bool WasCreatedStrongly;

    }

    /// <summary>
    /// Optional attribute for <see cref="INodeMemoryInputTag"/> indicating that <see cref="MemoryInputSystem"/> should relax its
    /// requirement for an exact match between the source ECS buffer type and the destination <see cref="NodeSet"/> <see cref="DataInput"/> of
    /// <see cref="Buffer{T}"/> type. Instead, The types need only match in size and alignment.
    /// </summary>
    [Obsolete]
    public class NativeAllowReinterpretationAttribute : Attribute { }

    /// <summary>
    /// Abstract base for a system that ticks a <see cref="NodeSet"/> by calling its <see cref="NodeSet.Update"/> in <see cref="OnUpdate"/>.
    /// If a conjunction of <see cref="NodeMemoryInput{TTag, TBufferToMove}"/> and <see cref="TBufferToMove"/> exists on an entity,
    /// it will specify a shallow buffer memory mapping from ECS to the <see cref="NodeSet"/>. Thus, every frame this holds the 
    /// ECS buffer will be directly available on the input port specified in <see cref="NodeMemoryInput{TTag, TBufferToMove}"/>.
    /// 
    /// The system will verify that target node input ports are of type <see cref="DataInput"/> of
    /// <see cref="Buffer{TBufferToMove}"/> of <see cref="TBufferToMove"/> (see <see cref="NativeAllowReinterpretationAttribute"/>).
    /// 
    /// To provide memory mappings for different node sets in a pipeline, subclass this 
    /// system with different concrete types of <see cref="TTag"/>.
    /// </summary>
    /// <typeparam name="TTag">
    /// A unique type tag used with <see cref="NodeMemoryInput{TTag, TBufferToMove}"/> to define the concrete component type picked up
    /// by this system.
    /// </typeparam>
    /// <typeparam name="TBufferToMove">
    /// An <see cref="IBufferElementData"/> type that this system should move to 
    /// any node/port pair specified in <see cref="NodeMemoryInput{TTag, TBufferToMove}"/>.
    /// </typeparam>
    /// <remarks> 
    /// Remember to call base functions of any functions you override.
    /// Any logical errors you make are notified as log errors every frame.
    /// </remarks>
    [Obsolete("Use ComponentNode instead")]
    public abstract class MemoryInputSystem<TTag, TBufferToMove> : JobComponentSystem
        where TTag : INodeMemoryInputTag
        where TBufferToMove : struct, IBufferElementData
    {
        internal class MainThreadChecker : ComponentSystem
        {
            static bool s_BypassStrongCheck = typeof(TBufferToMove).GetCustomAttributes(false).Any(a => a is NativeAllowReinterpretationAttribute);

            public MemoryInputSystem<TTag, TBufferToMove> Parent;

            protected override void OnUpdate()
            {
                Entities
                    .WithAll<NodeMemoryInput<TTag, TBufferToMove>, TBufferToMove>()
                    .ForEach(
                        (Entity e, ref NodeMemoryInput<TTag, TBufferToMove> input) =>
                        {
                            if (input.ResolvedDestination.Handle != default)
                                return;

                            var set = Parent.UnownedSet;

                            // TODO: Can remove this if it's OK to throw an exception.
                            if (!set.Exists(input.Node))
                            {
                                Debug.LogError("Node is disposed or invalid");
                                return;
                            }

                            var destination = new InputPair(set, input.Node, input.InternalPort);

                            if (!PerformRuntimeCheck(!input.WasCreatedStrongly, destination, e, input.Node))
                                return;

                            // All good, commit resolved stuff
                            input.ResolvedDestination = destination;
                        }
                    );

                Entities
                    .WithAll<NodeMemoryInput<TTag, TBufferToMove>>()
                    .WithNone<TBufferToMove>()
                    .ForEach(
                        (Entity e, ref NodeMemoryInput<TTag, TBufferToMove> input) =>
                        {
                            if (input.ResolvedDestination.Handle == default)
                                return;

                            input.ResolvedDestination = default;
                        }
                    );
            }

            bool PerformRuntimeCheck(bool shouldPerformWeakCheck, in InputPair destination, Entity e, NodeHandle original)
            {
                var set = Parent.UnownedSet;

                if (shouldPerformWeakCheck)
                {
                    var targetPort = set.GetFormalPort(destination);

                    if (destination.Port.IsArray != targetPort.IsPortArray)
                    {
                        Debug.LogError(targetPort.IsPortArray
                            ? "An array index is required when assigning a buffer to an array port."
                            : "An array index can only be given when assigning a buffer to an array port.");

                        return false;
                    }

                    if (s_BypassStrongCheck)
                    {
                        if (!targetPort.Type.IsConstructedGenericType || targetPort.Type.GetGenericTypeDefinition() != typeof(Buffer<>))
                        {
                            Debug.LogError("Cannot assign a buffer to a non-Buffer type port");
                            return false;
                        }

                        var bufferElementType = targetPort.Type.GetGenericArguments()[0];

                        if (UnsafeUtility.SizeOf(bufferElementType) != UnsafeUtility.SizeOf<TBufferToMove>())
                        {
                            Debug.LogError($"Cannot assign one buffer type to another fundamentally different type");
                            return false;
                        }
                    }
                    else if (targetPort.Type != typeof(Buffer<TBufferToMove>))
                    {
                        Debug.LogError($"Cannot assign a buffer of type {typeof(TBufferToMove)} from entity {e} to a port of type {targetPort.Type} " +
                            $"on node type {set.GetDefinition(original).GetType()}");
                        return false;
                    }
                }

                return set.ReportPortArrayBounds(destination);
            }
        }

        /// <summary>
        /// Assign a unique set here, which will be updated in <see cref="MemoryInputSystem{TTag, TBufferToMove}.OnUpdate(JobHandle)"/>.
        /// The set will not be disposed or cleaned up, accordingly, care must be taken to ensure that this system is no
        /// longer running once external code has disposed the set.
        /// </summary>
        protected NodeSet UnownedSet { get; set; }

        EntityQuery m_TriggerQuery, m_UpdateQuery;
        FutureArrayInputBatch m_InputBatch;
        MainThreadChecker m_MainThreadChecker;

        [BurstCompile]
        struct ClearBatch : IJob
        {
            public FutureArrayInputBatch InputBatch;

            public void Execute() => InputBatch.Clear();
        }

        [BurstCompile]
        struct MapDataFlowGraphInputDataToNodes : IJob
        {
            public FutureArrayInputBatch InputBatch;
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> Entities;

            public BufferFromEntity<TBufferToMove> BufferFromEntity;
            public ComponentDataFromEntity<NodeMemoryInput<TTag, TBufferToMove>> FlowGraphInput;


            public void Execute()
            {
                for(int i = 0; i < Entities.Length; ++i)
                {
                    var memoryInput = FlowGraphInput[Entities[i]];
                    if(memoryInput.ResolvedDestination.Handle != default)
                    {
                        var buffer = BufferFromEntity[Entities[i]];
                        InputBatch.SetTransientBuffer(memoryInput.ResolvedDestination, buffer.AsNativeArray());
                    }
                }
            }
        }

        protected override void OnCreate()
        {
            m_TriggerQuery = GetEntityQuery(
                new EntityQueryDesc
                {
                    Any = new[]
                    {
                        ComponentType.ReadOnly<TBufferToMove>(),
                        ComponentType.ReadWrite<NodeMemoryInput<TTag, TBufferToMove>>()
                    }
                }
            );

            m_UpdateQuery = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<TBufferToMove>(),
                        ComponentType.ReadWrite<NodeMemoryInput<TTag, TBufferToMove>>()
                    }
                }
            );

            m_InputBatch = new FutureArrayInputBatch(200, Allocator.Persistent);
            m_MainThreadChecker = World.GetOrCreateSystem<MainThreadChecker>();
            m_MainThreadChecker.Parent = this;

        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (UnownedSet == null)
                return inputDeps;

            // Awkward manual system update
            m_MainThreadChecker.Update();

            inputDeps = new ClearBatch { InputBatch = m_InputBatch }.Schedule(inputDeps);

            var map = new MapDataFlowGraphInputDataToNodes
            {
                InputBatch = m_InputBatch,
                Entities = m_UpdateQuery.ToEntityArray(Allocator.TempJob, out var memoryFence),
                BufferFromEntity = GetBufferFromEntity<TBufferToMove>(false), // TODO: Make true
                FlowGraphInput = GetComponentDataFromEntity<NodeMemoryInput<TTag, TBufferToMove>>(false) // TODO: Make true
            };

            inputDeps = map.Schedule(JobHandle.CombineDependencies(memoryFence, inputDeps));
            var batchHandle = UnownedSet.SubmitDeferredInputBatch(inputDeps, m_InputBatch);

            UnownedSet.Update();

            inputDeps = UnownedSet.GetBatchDependencies(batchHandle);

            return inputDeps;
        }

        protected override void OnDestroy()
        {
            m_InputBatch.Dispose();
        }
    }
}
