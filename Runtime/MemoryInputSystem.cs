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
    /// Base common interface for unique type tag used by <see cref="MemoryInputSystem{TTag, TBufferToMove}"/> and <see cref="NodeMemoryInput{TTag}"/>.
    /// </summary>
    public interface INodeMemoryInputTag { }

    /// <summary>
    /// Component for specifying a memory mapping from an entity to a node.
    /// </summary>
    /// <typeparam name="TTag">
    /// A unique type tag to associate memory mappings with a particular instantiation of
    /// <see cref="MemoryInputSystem{TTag, TBufferToMove}"/>.
    /// </summary>
    public struct NodeMemoryInput<TTag> : IComponentData
        where TTag : INodeMemoryInputTag
    {
        public NodeMemoryInput(NodeHandle node, InputPortID port)
        {
            Node = node;
            m_Port = new InputPortArrayID(port);
            ResolvedNode = default;
            ResolvedPort = default;
        }

        public NodeMemoryInput(NodeHandle node, InputPortID port, ushort arrayIndex)
        {
            Node = node;
            m_Port = new InputPortArrayID(port, arrayIndex);
            ResolvedNode = default;
            ResolvedPort = default;
        }

        public NodeHandle Node { get; private set; }
        public InputPortID Port => m_Port.PortID;
        public ushort ArrayIndex => m_Port.ArrayIndex;

        internal NodeHandle ResolvedNode;
        internal InputPortArrayID ResolvedPort;
        internal InputPortArrayID m_Port;
    }

    /// <summary>
    /// Optional attribute for <see cref="INodeMemoryInputTag"/> indicating that <see cref="MemoryInputSystem"/> should relax its
    /// requirement for an exact match between the source ECS buffer type and the destination <see cref="NodeSet"/> <see cref="DataInput"/> of
    /// <see cref="Buffer{T}"/> type. Instead, The types need only match in size and alignment.
    /// </summary>
    public class NativeAllowReinterpretationAttribute : Attribute { }

    /// <summary>
    /// Abstract base for a system that ticks a <see cref="NodeSet"/> by calling its <see cref="NodeSet.Update"/> in <see cref="OnUpdate"/>.
    /// If a conjunction of <see cref="NodeMemoryInput{TTag}"/> and <see cref="TBufferToMove"/> exists on an entity,
    /// it will specify a shallow buffer memory mapping from ECS to the <see cref="NodeSet"/>. Thus, every frame this holds the 
    /// ECS buffer will be directly available on the input port specified in <see cref="NodeMemoryInput{TTag}"/>.
    /// 
    /// The system will verify that target node input ports are of type <see cref="DataInput"/> of
    /// <see cref="Buffer{TBufferToMove}"/> of <see cref="TBufferToMove"/> (see <see cref="NativeAllowReinterpretationAttribute"/>).
    /// 
    /// To provide memory mappings for different node sets in a pipeline, subclass this 
    /// system with different concrete types of <see cref="TTag"/>.
    /// </summary>
    /// <typeparam name="TTag">
    /// A unique type tag used with <see cref="NodeMemoryInput{TTag}"/> to define the concrete component type picked up
    /// by this system.
    /// </typeparam>
    /// <typeparam name="TBufferToMove">
    /// An <see cref="IBufferElementData"/> type that this system should move to 
    /// any node/port pair specified in <see cref="NodeMemoryInput{TTag}"/>.
    /// </typeparam>
    /// <remarks> 
    /// Remember to call base functions of any functions you override.
    /// Any logical errors you make are notified as log errors every frame.
    /// </remarks>
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
                    .WithAll<NodeMemoryInput<TTag>, TBufferToMove>()
                    .ForEach(
                        (Entity e, ref NodeMemoryInput<TTag> input) =>
                        {
                            if (input.ResolvedNode != default)
                                return;

                            var set = Parent.UnownedSet;

                            if (!set.Exists(input.Node))
                            {
                                Debug.LogError("Node is disposed or invalid");
                                return;
                            }

                            var klass = set.GetFunctionality(input.Node);
                            var ports = klass.GetPortDescription(input.Node);
                            var targetPort = ports.Inputs[input.Port.Port];

                            if (input.m_Port.IsArray != targetPort.IsPortArray)
                            {
                                Debug.LogError(targetPort.IsPortArray
                                    ? "An array index is required when assigning a buffer to an array port."
                                    : "An array index can only be given when assigning a buffer to an array port.");
                                return;
                            }

                            if (input.m_Port.IsArray && input.m_Port.ArrayIndex >= set.GetPortArraySize_Unchecked(input.Node, input.m_Port.PortID))
                            {
                                Debug.LogError("PortArray index out of bounds.");
                                return;
                            }

                            if (s_BypassStrongCheck)
                            {
                                if (!targetPort.Type.IsConstructedGenericType || targetPort.Type.GetGenericTypeDefinition() != typeof(Buffer<>))
                                {
                                    Debug.LogError("Cannot assign a buffer to a non-Buffer type port");
                                    return;
                                }

                                var bufferElementType = targetPort.Type.GetGenericArguments()[0];

                                if (UnsafeUtility.SizeOf(bufferElementType) != UnsafeUtility.SizeOf<TBufferToMove>())
                                {
                                    Debug.LogError($"Cannot assign one buffer type to another fundamentally different type");
                                    return;
                                }
                            }
                            else if (targetPort.Type != typeof(Buffer<TBufferToMove>))
                            {
                                Debug.LogError($"Cannot assign a buffer of type {typeof(TBufferToMove)} from entity {e} to a port of type {targetPort.Type} on node type {klass.GetType()}");
                                return;
                            }

                            var inputPort = input.m_Port;
                            var targetNode = input.Node;

                            set.ResolvePublicDestination(ref targetNode, ref inputPort);

                            input.ResolvedNode = targetNode;
                            input.ResolvedPort = inputPort;
                        }
                    );

                Entities
                    .WithAll<NodeMemoryInput<TTag>>()
                    .WithNone<TBufferToMove>()
                    .ForEach(
                        (Entity e, ref NodeMemoryInput<TTag> input) =>
                        {
                            if (input.ResolvedNode == default)
                                return;

                            input.ResolvedNode = default;
                            input.ResolvedPort = default;
                        }
                    );
            }
        }

        /// <summary>
        /// Assign a unique set here, which will be updated in <see cref="MemoryInputSystem{TNodeMemoryInput, TBufferToMove}.OnUpdate(JobHandle)"/>.
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
            public ComponentDataFromEntity<NodeMemoryInput<TTag>> FlowGraphInput;


            public void Execute()
            {
                for(int i = 0; i < Entities.Length; ++i)
                {
                    var resolved = FlowGraphInput[Entities[i]];
                    if(resolved.ResolvedNode != default)
                    {
                        var buffer = BufferFromEntity[Entities[i]];
                        InputBatch.SetTransientBuffer(resolved.ResolvedNode, resolved.ResolvedPort, buffer.AsNativeArray());
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
                        ComponentType.ReadWrite<NodeMemoryInput<TTag>>()
                    }
                }
            );

            m_UpdateQuery = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<TBufferToMove>(),
                        ComponentType.ReadWrite<NodeMemoryInput<TTag>>()
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
                FlowGraphInput = GetComponentDataFromEntity<NodeMemoryInput<TTag>>(false) // TODO: Make true
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
