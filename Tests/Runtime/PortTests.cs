using System;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Unity.DataFlowGraph.Tests
{
    class PortTests
    {
        [Preserve] // avoid stripping, [Values()] is not enough
        struct Scalar : IComponentData
        {

        }

        [Preserve] // avoid stripping, [Values()] is not enough
        struct SystemScalar : ISystemStateComponentData
        {

        }

        [Preserve] // avoid stripping, [Values()] is not enough
        struct Buffer : IBufferElementData
        {

        }

        [Preserve] // avoid stripping, [Values()] is not enough
        struct SystemBuffer : ISystemStateBufferElementData
        {

        }

        [Test]
        public void PortStorageFlag_IsNotInLow16bits()
        {
            Assert.GreaterOrEqual(PortStorage.InternalFlag, 1 << 16);
            Assert.GreaterOrEqual(1 << 30, PortStorage.InternalFlag);
        }

        [Test]
        public void PortStorage_CanBeInitialized_FromUInt16_AndRetrieveValue_ThroughPortAccessor([Values((ushort)0u, (ushort)1u, (ushort)13u, (ushort)(1 << 16 - 1))] ushort ushortValue)
        {
            Assert.AreEqual(new PortStorage(ushortValue).DFGPortIndex, ushortValue);
        }

        [Test]
        public void PortStorage_CanBeInitialized_FromECSTypes_AndMatchTypeIndex([Values(typeof(Scalar), typeof(SystemScalar), typeof(Buffer), typeof(SystemBuffer))] Type ecsType)
        {
            var component = new ComponentType(ecsType);
            Assert.AreEqual(new PortStorage(component).ECSTypeIndex, component.TypeIndex);
        }

        [Test]
        public void PortStorage_ReadComponentType_HasReadOnlyFlag([Values(typeof(Scalar), typeof(SystemScalar), typeof(Buffer), typeof(SystemBuffer))] Type ecsType)
        {
            var component = new ComponentType(ecsType);
            var storage = new PortStorage(component);
            var restoredReadOnlyComponent = storage.ReadOnlyComponentType;

            Assert.AreEqual(restoredReadOnlyComponent.AccessModeType, ComponentType.AccessMode.ReadOnly);
            Assert.AreEqual(restoredReadOnlyComponent.TypeIndex, component.TypeIndex);
        }

        [Test]
        public void PortStorage_ReadWriteComponentType_HasReadWriteFlag([Values(typeof(Scalar), typeof(SystemScalar), typeof(Buffer), typeof(SystemBuffer))] Type ecsType)
        {
            var component = new ComponentType(ecsType);
            var storage = new PortStorage(component);
            var restoredReadOnlyComponent = storage.ReadWriteComponentType;

            Assert.AreEqual(restoredReadOnlyComponent.AccessModeType, ComponentType.AccessMode.ReadWrite);
            Assert.AreEqual(restoredReadOnlyComponent.TypeIndex, component.TypeIndex);
        }

        [Test]
        public void PortStorageConstructors_CorrectlyTagUnion()
        {
            Assert.False(new PortStorage((ushort)0u).IsECSPort);
            Assert.False(new PortStorage(0).IsECSPort);
            Assert.True(new PortStorage(new ComponentType()).IsECSPort);
        }
    }

}
