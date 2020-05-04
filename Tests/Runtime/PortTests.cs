using System;
using System.Collections.Generic;
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
        public void PortStorageFlags_AreNotInLow16bits()
        {
            Assert.GreaterOrEqual(PortStorage.IsECSPortFlag, 1 << 16);
            Assert.GreaterOrEqual(1 << 30, PortStorage.IsECSPortFlag);
            Assert.GreaterOrEqual(PortStorage.IsDFGPortFlag, 1 << 16);
            Assert.GreaterOrEqual(1 << 30, PortStorage.IsDFGPortFlag);
        }

        [Test]
        public void DefaultConstructed_PortStorage_IsValid()
        {
            var defaultPortStorage = new PortStorage();
            Assert.IsFalse(defaultPortStorage.IsECSPort);
            Assert.IsFalse(defaultPortStorage.IsDFGPort);
#if DFG_ASSERTIONS
            ushort port;
            int componentType;
            Assert.Throws<AssertionException>(() => port = defaultPortStorage.DFGPortIndex);
            Assert.Throws<AssertionException>(() => componentType = defaultPortStorage.ECSTypeIndex);
#endif
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

        [Test]
        public void ConnectionCategories_DoNotClash()
        {
            var connectionCategories = new List<uint>();
            foreach (PortDescription.Category portCategory in Enum.GetValues(typeof(PortDescription.Category)))
            {
                foreach (PortDescription.CategoryShift shift in Enum.GetValues(typeof(PortDescription.CategoryShift)))
                {
                    if (shift == PortDescription.CategoryShift.Max)
                        continue;
                    var connectionCategory = (uint) portCategory << (int) shift;
                    connectionCategories.Add(connectionCategory);
                }
            }

            connectionCategories.Add(PortDescription.MessageToDataConnectionCategory);

            CollectionAssert.AllItemsAreUnique(connectionCategories);
            Assert.AreEqual(10, connectionCategories.Count);
        }
    }
}
