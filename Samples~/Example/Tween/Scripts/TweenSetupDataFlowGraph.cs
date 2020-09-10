using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
    

namespace Unity.DataFlowGraph.Examples.RenderGraph
{
    public class TweenSetupDataFlowGraph : MonoBehaviour
    {

        public GameObject ObjectPrefab;

        [Range(0, 1)]
        public float Tween;

        [Range(0, 10)]
        public float VerticalSpeed;

        [Range(0, 10)]
        public float HorizontalSpeed;

        public Vector3 Rotation;

        public int Count;
        public int TouchCount;

        public NodeSet.RenderExecutionModel ExecutionMode;
        bool DisregardOrder;
        bool DisregardType;

        float m_Tween, m_VerticalSpeed, m_HorizontalSpeed;

        Vector3 m_Rotation;

        NodeSet m_Set;

        List<TweeningObject> m_AnimationGraphs = new List<TweeningObject>();

        struct TweeningObject : IDisposable
        {
            public NodeHandle<PositionAnimator> Vertical, Horizontal;
            public NodeHandle<AnimationMixer> Mixer;
            public NodeHandle<DirectionRotator> Rotator;
            public GameObject GO;
            NodeSet m_Set;

            public TweeningObject(NodeSet set, GameObject prefab, float axisRotation)
            {
                GO = Object.Instantiate(prefab);
                m_Set = set;

                Vertical = set.Create<PositionAnimator>();
                set.SendMessage(Vertical, PositionAnimator.SimulationPorts.Time, axisRotation);
                set.SendMessage(Vertical, PositionAnimator.SimulationPorts.Movement, Vector3.up);

                Horizontal = set.Create<PositionAnimator>();
                set.SendMessage(Horizontal, PositionAnimator.SimulationPorts.Time, axisRotation);
                set.SendMessage(Horizontal, PositionAnimator.SimulationPorts.Movement, Vector3.left);

                Mixer = set.Create<AnimationMixer>();

                Rotator = set.Create<DirectionRotator>();
                set.SendMessage(Rotator, DirectionRotator.SimulationPorts.Magnitude, axisRotation);
                set.SendMessage(Rotator, DirectionRotator.SimulationPorts.TransformTarget, GO.transform);

                m_Set.Connect(Vertical, PositionAnimator.KernelPorts.Output, Mixer, AnimationMixer.KernelPorts.InputA);
                m_Set.Connect(Horizontal, PositionAnimator.KernelPorts.Output, Mixer, AnimationMixer.KernelPorts.InputB);
                m_Set.Connect(Mixer, AnimationMixer.KernelPorts.Output, Rotator, DirectionRotator.KernelPorts.Input);
            }

            public void Touch()
            {
                NodeHandle thing = Mixer;
                m_Set.Disconnect(Mixer, AnimationMixer.KernelPorts.Output, Rotator, DirectionRotator.KernelPorts.Input);
                m_Set.Connect(Mixer, AnimationMixer.KernelPorts.Output, Rotator, DirectionRotator.KernelPorts.Input);

            }

            public void Dispose()
            {
                m_Set.Destroy(Horizontal);
                m_Set.Destroy(Vertical);
                m_Set.Destroy(Mixer);
                m_Set.Destroy(Rotator);
                Object.Destroy(GO);
            }
        }

        private void OnGUI()
        {
            if(GUI.Button(new Rect(5, 5, 100, 30), "Topology recompute"))
            {
                var stride = m_AnimationGraphs.Count / TouchCount;
                for(int i = 0; i < TouchCount; ++i)
                {
                    m_AnimationGraphs[i * stride].Touch();
                }
            }
        }

        void OnEnable()
        {
            m_Rotation = new Vector3();
            m_VerticalSpeed = m_HorizontalSpeed = m_Tween = 0;

            m_Set = new NodeSet();

            for (int i = 0; i < Count; ++i)
            {
                m_AnimationGraphs.Add(new TweeningObject(m_Set, ObjectPrefab, (2 * Mathf.PI * i) / (Count)));
            }
        }

        void OnDisable() => Cleanup(true);

        void OnUpdateGraph()
        {
            if (m_Tween != Tween)
            {
                m_Tween = Tween;
                for(int i = 0; i < m_AnimationGraphs.Count; ++i)
                    m_Set.SendMessage(m_AnimationGraphs[i].Mixer, AnimationMixer.SimulationPorts.Blend, m_Tween);
            }

            if (m_HorizontalSpeed != HorizontalSpeed)
            {
                m_HorizontalSpeed = HorizontalSpeed;
                for (int i = 0; i < m_AnimationGraphs.Count; ++i)
                    m_Set.SendMessage(m_AnimationGraphs[i].Horizontal, PositionAnimator.SimulationPorts.Speed, m_HorizontalSpeed);
            }

            if (m_VerticalSpeed != VerticalSpeed)
            {
                m_VerticalSpeed = VerticalSpeed;
                for (int i = 0; i < m_AnimationGraphs.Count; ++i)
                    m_Set.SendMessage(m_AnimationGraphs[i].Vertical, PositionAnimator.SimulationPorts.Speed, m_VerticalSpeed);
            }

            if (Rotation != m_Rotation)
            {
                m_Rotation = Rotation;
                for (int i = 0; i < m_AnimationGraphs.Count; ++i)
                    m_Set.SendMessage(m_AnimationGraphs[i].Rotator, DirectionRotator.SimulationPorts.Magnitude, m_Rotation.x);
            }

            m_Set.RendererModel = ExecutionMode;

            m_Set.Update();
        }

        void Cleanup(bool emergency)
        {
            if (m_Set != null)
            {
                m_AnimationGraphs.ForEach(t => t.Dispose());
                m_Set.Dispose();
                m_Set = null;
            }

            m_AnimationGraphs.Clear();
        }

        void Update()
        {
            OnUpdateGraph();
        }
    }

}
