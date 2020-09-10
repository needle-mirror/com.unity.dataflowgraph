using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.DataFlowGraph;

namespace Unity.DataFlowGraph.TimeExample
{
    using TimeStandards;
    using StreamType = Unity.Mathematics.float2;

    namespace TimeStandards
    {
        struct SeekMessage
        {
            public float Time;
        }

        struct SpeedMessage
        {
            public float Scale;
        }

        struct PlayStateMessage
        {
            public bool ShouldPlay;
        }

        struct WeightMessage
        {
            public float Gradient;
        }

        struct TimeOffsetMessage
        {
            public float Origin;
        }

        interface ISeekable : ITaskPort<ISeekable> { }
        interface ISpeed : ITaskPort<ISpeed> { }
        interface IPlayable : ITaskPort<IPlayable> { }
        interface IWeightable : ITaskPort<IWeightable> { }
        interface IOffsettable : ITaskPort<IOffsettable> { }
    }

    class Generator : SimulationKernelNodeDefinition<Generator.SimPorts,Generator.KernelDefs>, ISeekable, ISpeed, IPlayable
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<Generator, SeekMessage> SeekPort;
            public MessageInput<Generator, SpeedMessage> SpeedPort;
            public MessageInput<Generator, PlayStateMessage> PlayPort;
            public MessageInput<Generator, StreamType> OutputMask;
            #pragma warning restore 649
        }

        InputPortID ITaskPort<ISeekable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SeekPort;
        InputPortID ITaskPort<ISpeed>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SpeedPort;
        InputPortID ITaskPort<IPlayable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.PlayPort;

        public struct KernelDefs : IKernelPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public DataOutput<Generator, StreamType> Output;
            #pragma warning restore 649
        }

        struct KernelData : IKernelData
        {
            public float Time;
            public StreamType Mask;
        }

        struct Data : INodeData, IMsgHandler<SeekMessage>, IMsgHandler<SpeedMessage>, IMsgHandler<PlayStateMessage>, IMsgHandler<StreamType>, IUpdate
        {
            float m_Time;
            float m_Speed;
            bool m_IsPlaying;
            KernelData m_KernelData;

            public void HandleMessage(in MessageContext ctx, in SeekMessage msg) => m_Time = msg.Time;
            public void HandleMessage(in MessageContext ctx, in SpeedMessage msg) => m_Speed = msg.Scale;
            public void HandleMessage(in MessageContext ctx, in PlayStateMessage msg) => m_IsPlaying = msg.ShouldPlay;
            public void HandleMessage(in MessageContext ctx, in StreamType msg)
            {
                m_KernelData.Mask = msg;
                ctx.UpdateKernelData(m_KernelData);
            }

            public void Update(in UpdateContext ctx)
            {
                if (!m_IsPlaying)
                    return;

                m_Time += Time.deltaTime * m_Speed;
                m_KernelData.Time = m_Time;
                ctx.UpdateKernelData(m_KernelData);
            }
        }

        struct Kernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                math.sincos(data.Time * 2 * (float)Math.PI, out float x, out float y);
                ctx.Resolve(ref ports.Output) = data.Mask * new StreamType(x, y);
            }
        }
    }

    class Mixer : SimulationKernelNodeDefinition<Mixer.SimPorts, Mixer.KernelDefs>, IWeightable
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<Mixer, WeightMessage> WeightPort;
            #pragma warning restore 649
        }

        InputPortID ITaskPort<IWeightable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.WeightPort;

        struct Data : INodeData, IMsgHandler<WeightMessage>
        {
            public void HandleMessage(in MessageContext ctx, in WeightMessage msg) 
                => ctx.UpdateKernelData(new KernelData { Gradient = msg.Gradient });
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public DataInput<Mixer, StreamType> Left;
            public DataInput<Mixer, StreamType> Right;
            public DataOutput<Mixer, StreamType> Output;
            #pragma warning restore 649
        }

        struct KernelData : IKernelData
        {
            public float Gradient;
        }

        struct Kernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = math.lerp(ctx.Resolve(ports.Left), ctx.Resolve(ports.Right), data.Gradient);
            }
        }

    }

    class ClipContainer 
        : SimulationNodeDefinition<ClipContainer.SimPorts>
        , ISeekable
        , ISpeed
        , IPlayable
        , IOffsettable
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<ClipContainer, SeekMessage> SeekPort;
            public MessageInput<ClipContainer, SpeedMessage> SpeedPort;
            public MessageInput<ClipContainer, PlayStateMessage> PlayPort;
            public MessageInput<ClipContainer, TimeOffsetMessage> TimeOffset;

            public MessageOutput<ClipContainer, SeekMessage> SeekOut;
            public MessageOutput<ClipContainer, SpeedMessage> SpeedOut;
            public MessageOutput<ClipContainer, PlayStateMessage> PlayOut;
            #pragma warning restore 649
        }

        InputPortID ITaskPort<ISeekable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SeekPort;
        InputPortID ITaskPort<ISpeed>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SpeedPort;
        InputPortID ITaskPort<IPlayable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.PlayPort;
        InputPortID ITaskPort<IOffsettable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.TimeOffset;

        public struct Data 
            : INodeData
            , IMsgHandler<SeekMessage>
            , IMsgHandler<SpeedMessage>
            , IMsgHandler<PlayStateMessage>
            , IMsgHandler<TimeOffsetMessage>
            , IUpdate
        {
            float m_Time;
            float m_Speed;
            float m_Origin;
            bool m_IsPlaying;
            bool m_WasPlaying;

            public void HandleMessage(in MessageContext ctx, in SeekMessage msg)
            {
                m_Time = msg.Time;
                ctx.EmitMessage(SimulationPorts.SeekOut, new SeekMessage { Time = m_Time - m_Origin });
            }

            public void HandleMessage(in MessageContext ctx, in SpeedMessage msg)
            {
                m_Speed = msg.Scale;
                ctx.EmitMessage(SimulationPorts.SpeedOut, msg);
            }

            public void HandleMessage(in MessageContext ctx, in PlayStateMessage msg) => m_IsPlaying = msg.ShouldPlay;
            public void HandleMessage(in MessageContext ctx, in TimeOffsetMessage msg) => m_Origin = msg.Origin;

            public void Update(in UpdateContext ctx)
            {
                if (m_IsPlaying)
                {
                    m_Time += Time.deltaTime * m_Speed;
                    if (m_Time >= m_Origin && !m_WasPlaying)
                    {
                        ctx.EmitMessage(SimulationPorts.PlayOut, new PlayStateMessage { ShouldPlay = true });
                        m_WasPlaying = true;
                    }
                    else if (m_WasPlaying)
                    {
                        ctx.EmitMessage(SimulationPorts.PlayOut, new PlayStateMessage { ShouldPlay = false });
                        m_WasPlaying = false;
                    }
                }
                else if (m_WasPlaying)
                {
                    ctx.EmitMessage(SimulationPorts.PlayOut, new PlayStateMessage { ShouldPlay = false });
                    m_WasPlaying = false;
                }
            }
        }


    }

    class TimelineAnchor : SimulationNodeDefinition<TimelineAnchor.SimPorts>, ISeekable, ISpeed, IPlayable
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public MessageInput<TimelineAnchor, SeekMessage> SeekPort;
            public MessageInput<TimelineAnchor, SpeedMessage> SpeedPort;
            public MessageInput<TimelineAnchor, PlayStateMessage> PlayPort;

            public MessageOutput<TimelineAnchor, SeekMessage> SeekOut;
            public MessageOutput<TimelineAnchor, SpeedMessage> SpeedOut;
            public MessageOutput<TimelineAnchor, PlayStateMessage> PlayOut;
            #pragma warning restore 649
        }

        struct Data : INodeData, IMsgHandler<SeekMessage>, IMsgHandler<SpeedMessage>, IMsgHandler<PlayStateMessage>
        {
            public void HandleMessage(in MessageContext ctx, in SeekMessage msg) => ctx.EmitMessage(SimulationPorts.SeekOut, msg);
            public void HandleMessage(in MessageContext ctx, in SpeedMessage msg) => ctx.EmitMessage(SimulationPorts.SpeedOut, msg);
            public void HandleMessage(in MessageContext ctx, in PlayStateMessage msg) => ctx.EmitMessage(SimulationPorts.PlayOut, msg);
        }

        InputPortID ITaskPort<ISeekable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SeekPort;
        InputPortID ITaskPort<ISpeed>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.SpeedPort;
        InputPortID ITaskPort<IPlayable>.GetPort(NodeHandle handle) => (InputPortID)SimulationPorts.PlayPort;
    }


    public class TimeExample : MonoBehaviour
    {
        [Range(-1, 1)]
        public float X;

        [Range(-1, 1)]
        public float Y;

        [Range(0, 2)]
        public float Speed = 0.5f;

        [Range(0, 1)]
        public float MixerBlend = 0.25f;

        [Range(0, 10)]
        public float RestartSeekPoint = 2.0f;

        public bool Playing = true;

        NodeSet m_Set;

        NodeHandle<TimelineAnchor> m_Timeline;
        NodeHandle<Mixer> m_LastMixer;

        NativeList<NodeHandle> m_Nodes;

        GraphValue<StreamType> m_GraphOutput;

        float m_Speed, m_MixerBlend;
        bool m_Playing;

        void AddClip(NodeHandle<TimelineAnchor> timeline, float timeOffset, StreamType mask)
        {
            var leafSource = m_Set.Create<Generator>();
            var clip = m_Set.Create<ClipContainer>();
            var mixer = m_Set.Create<Mixer>();

            var leafAdapter = m_Set.Adapt(leafSource);
            var clipAdapter = m_Set.Adapt(clip);

            // Connect leaf into a clip, that handles time translation
            m_Set.Connect(clip, ClipContainer.SimulationPorts.PlayOut, leafAdapter.To<IPlayable>());
            m_Set.Connect(clip, ClipContainer.SimulationPorts.SpeedOut, leafAdapter.To<ISpeed>());
            m_Set.Connect(clip, ClipContainer.SimulationPorts.SeekOut, leafAdapter.To<ISeekable>());

            // connect clip to timeline anchor
            m_Set.Connect(timeline, TimelineAnchor.SimulationPorts.PlayOut, clipAdapter.To<IPlayable>());
            m_Set.Connect(timeline, TimelineAnchor.SimulationPorts.SpeedOut, clipAdapter.To<ISpeed>());
            m_Set.Connect(timeline, TimelineAnchor.SimulationPorts.SeekOut, clipAdapter.To<ISeekable>());
            
            // set up params on clip and generator
            m_Set.SendMessage(m_Set.Adapt(clip).To<IOffsettable>(), new TimeOffsetMessage { Origin = timeOffset });
            m_Set.SendMessage(leafSource, Generator.SimulationPorts.OutputMask, mask);

            // push back an animation tree, and connect a new mixer to the top
            m_Set.Connect(leafSource, Generator.KernelPorts.Output, mixer, Mixer.KernelPorts.Left);
            m_Set.Connect(m_LastMixer, Mixer.KernelPorts.Output, mixer, Mixer.KernelPorts.Right);

            m_Nodes.Add(mixer);
            m_Nodes.Add(clip);
            m_Nodes.Add(leafSource);

            m_Set.ReleaseGraphValue(m_GraphOutput);
            m_GraphOutput = m_Set.CreateGraphValue(mixer, Mixer.KernelPorts.Output);

            m_LastMixer = mixer;
        }

        void OnEnable()
        {
            m_Set = new NodeSet();
            m_Nodes = new NativeList<NodeHandle>(Allocator.Persistent);
            m_Timeline = m_Set.Create<TimelineAnchor>();
            m_LastMixer = m_Set.Create<Mixer>();
            m_Nodes.Add(m_LastMixer);
            m_Nodes.Add(m_Timeline);
            m_GraphOutput = m_Set.CreateGraphValue(m_LastMixer, Mixer.KernelPorts.Output);


            AddClip(m_Timeline, 1, new float2(1, 0));
            AddClip(m_Timeline, 2, new float2(0, 1));
        }

        void OnDisable()
        {
            for(int i = 0; i < m_Nodes.Length; ++i)
                m_Set.Destroy(m_Nodes[i]);

            m_Set.ReleaseGraphValue(m_GraphOutput);
            m_Nodes.Dispose();
            m_Set.Dispose();
        }

        void Update()
        {
            // Controlling the whole timeline, and every clip playing in it:

            if (m_Speed != Speed)
            {
                m_Speed = Speed;
                m_Set.SendMessage(m_Timeline, TimelineAnchor.SimulationPorts.SpeedPort, new SpeedMessage { Scale = m_Speed });
            }

            if (m_MixerBlend != MixerBlend)
            {
                m_MixerBlend = MixerBlend;
                m_Set.SendMessage(m_LastMixer, Mixer.SimulationPorts.WeightPort, new WeightMessage{ Gradient = m_MixerBlend});
            }

            if (m_Playing != Playing)
            {
                m_Playing = Playing;
                if (m_Playing)
                {
                    m_Set.SendMessage(m_Timeline, TimelineAnchor.SimulationPorts.SeekPort, new SeekMessage { Time = RestartSeekPoint });
                }
                m_Set.SendMessage(m_Timeline, TimelineAnchor.SimulationPorts.PlayPort, new PlayStateMessage { ShouldPlay = m_Playing });
            }

            // advancing time:
            m_Set.Update();

            // getting output out of the graph:
            var current = m_Set.GetValueBlocking(m_GraphOutput);
            X = current.x;
            Y = current.y;
        }
    }
}
