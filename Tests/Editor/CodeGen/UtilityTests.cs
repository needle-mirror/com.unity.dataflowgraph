using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using NUnit.Framework;

namespace Unity.DataFlowGraph.CodeGen.Tests
{

    public class UtilityTests
    {

        [Test]
        public void Diagnostic_WithErrorsOrExceptions_ConfirmsHasErrors()
        {
            var diag = new Diag();
            Assert.False(diag.HasErrors());

            diag.TestingError();
            Assert.True(diag.HasErrors());

            diag = new Diag();

            diag.DFG_IE_04(new System.Exception(""));
            Assert.True(diag.HasErrors());
        }

        [Test]
        public void Diagnostic_WithWarning_DoesNotHaveErrors()
        {
            var diag = new Diag();
            diag.TestingWarning();
            Assert.False(diag.HasErrors());
        }
        class Base
        {
            public virtual void Function() { }
        }

        class Sub : Base
        {
            public override void Function()
            {
                base.Function();
            }
        }

        class Sub_NotOverridden : Base
        {
            public new void Function()
            {
                base.Function();
            }
        }

        [Test]
        public void Overrides_Extension_Works()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var subType = module.ImportReference(typeof(Sub));
                var subOverriddenType = module.ImportReference(typeof(Sub_NotOverridden));

                var func = module.ImportReference(typeof(Base).GetMethod("Function"));

                Assert.True(subType.Resolve().Overrides(func));
                Assert.False(subOverriddenType.Resolve().Overrides(func));
            }
        }

        interface INeededToBeImplemented { }
        interface IUnrelated { }

        struct SomethingThatImplements : INeededToBeImplemented { }

        [Test]
        public void IsOrImplements_Extension_WorksForValueTypes_AndInterfaces()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var subType = module.ImportReference(typeof(SomethingThatImplements));
                var baseInterface = module.ImportReference(typeof(INeededToBeImplemented));
                var unrelatedBase = module.ImportReference(typeof(IUnrelated));

                Assert.True(subType.IsOrImplements(subType));
                Assert.True(subType.IsOrImplements(baseInterface));
                Assert.False(subType.IsOrImplements(unrelatedBase));
            }
        }

        [Test]
        public void IsOrImplements_Extension_WorksForClasses()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var subType = module.ImportReference(typeof(Sub));
                var baseClass = module.ImportReference(typeof(Base));

                Assert.True(subType.IsOrImplements(subType));
                Assert.True(subType.IsOrImplements(baseClass));
            }
        }

        interface IGenericInterface<A> { }

        struct SpecificImplementer : IGenericInterface<int> { }

        [Test]
        public void IsOrImplements_MatchesClosedInterface_AgainstOpenInterface()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var openIface = module.ImportReference(typeof(IGenericInterface<>));
                var someType = module.ImportReference(typeof(SpecificImplementer));

                Assert.True(someType.IsOrImplements(openIface));
            }
        }

        [Test]
        public void IsOrImplements_OnlyMatches_IdenticalInstantiation()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var openIface = module.ImportReference(typeof(IGenericInterface<>));
                var someType = module.ImportReference(typeof(SpecificImplementer));

                var notTheRightIFace = openIface.MakeGenericInstanceType(module.TypeSystem.Int16);
                var theRightIFace = openIface.MakeGenericInstanceType(module.TypeSystem.Int32);

                Assert.False(someType.IsOrImplements(notTheRightIFace));
                Assert.True(someType.IsOrImplements(theRightIFace));
            }
        }

        [Test]
        public void RefersToSame_Works()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var subType = module.ImportReference(typeof(Sub));
                var baseClass = module.ImportReference(typeof(Base));

                Assert.True(subType.RefersToSame(subType));
                Assert.True(subType.RefersToSame(module.ImportReference(typeof(Sub))));

                Assert.True(subType.RefersToSame(subType));
                Assert.True(module.ImportReference(typeof(Sub)).RefersToSame(subType));

                Assert.False(subType.RefersToSame(baseClass));
                Assert.False(subType.RefersToSame(null));
            }
        }

        class GenericClass<A, B> { }
        class OpenGenericProvider<X, Y> { }

        [Test]
        public void RefersToSame_DoesNotReturnTrue_ForDifferentInstantiation_AndOpenClose_Mismatch()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var open = module.ImportReference(typeof(GenericClass<,>));

                var closedA = open.MakeGenericInstanceType(module.TypeSystem.UInt16, module.TypeSystem.UInt32);
                var closedB = open.MakeGenericInstanceType(module.TypeSystem.UInt32, module.TypeSystem.UInt16);

                Assert.False(open.RefersToSame(closedA));
                Assert.False(open.RefersToSame(closedB));

                Assert.False(closedA.RefersToSame(open));
                Assert.False(closedB.RefersToSame(open));

                Assert.False(closedA.RefersToSame(closedB));
                Assert.False(closedB.RefersToSame(closedA));

                Assert.True(closedA.Open().RefersToSame(open));
                Assert.True(closedB.Open().RefersToSame(open));
                Assert.True(closedB.Open().RefersToSame(closedA.Open()));
            }
        }


        [Test]
        public void TemplateSubstitution_CanInstantiate_SimpleGenericClass_AsOpen()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;

                // GenericClass`2
                var definition = module.ImportReference(typeof(GenericClass<,>)).Open();
                // TemplateParameterProvider`2
                var provider = module.ImportReference(typeof(OpenGenericProvider<,>)).Open();

                // GenericClass<A, B>
                var subType = definition.MakeGenericInstanceType(definition.GenericParameters.Cast<TypeReference>().ToArray());

                // GenericClass<X, Y>
                var open = HelperExtensions.InstantiateOpenTemplate_ForTesting(subType, new Collection<TypeReference>(provider.GenericParameters.Cast<TypeReference>().ToArray())) as GenericInstanceType;

                for (int i = 0; i < provider.GenericParameters.Count; ++i)
                    Assert.AreEqual(provider.GenericParameters[i], open.GenericArguments[i]);
            }
        }

        [Test]
        public void TemplateSubstitution_CanInstantiate_SimpleGenericClass_AsClosed()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                // GenericClass`2
                var definition = module.ImportReference(typeof(GenericClass<,>)).Open();
                // GenericClass<A, B>
                var subType = definition.MakeGenericInstanceType(definition.GenericParameters.Cast<TypeReference>().ToArray());
                // <int, float>
                var closedArgs = new Collection<TypeReference> { module.TypeSystem.Int32, module.TypeSystem.Single };
                // GenericClass<int, float>
                var closed = HelperExtensions.InstantiateOpenTemplate_ForTesting(subType, closedArgs) as GenericInstanceType;

                for (int i = 0; i < subType.GenericArguments.Count; ++i)
                    Assert.AreEqual(closedArgs[i], closed.GenericArguments[i]);
            }
        }

        class NestedGenericExpressionClass<W, Q> : GenericClass<OpenGenericProvider<Q, GenericClass<Q, W>>, W> { }

        [Test]
        public void TemplateSubstitution_CanInstantiate_NestedTypeExpression()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                // NestedGenericExpressionClass`2
                var definition = module.ImportReference(typeof(NestedGenericExpressionClass<,>)).Resolve();

                // TemplateParameterProvider`2
                var provider = module.ImportReference(typeof(OpenGenericProvider<,>)).Open();

                var W = definition.GenericParameters[0];
                var Y = provider.GenericParameters[1];

                // <Y, W>
                var closedArgs = new Collection<TypeReference> { Y, W };

                // GenericClass<OpenGenericProvider<NestedGenericExpressionClass`2.W, GenericClass<NestedGenericExpressionClass`2.W, NestedGenericExpressionClass`2.Q>>, NestedGenericExpressionClass`2.Q>
                var baseType = definition.BaseType;

                // NestedGenericExpressionClass`2.Q -> Y, NestedGenericExpressionClass`2.W -> W
                //  GenericClass<
                //      OpenGenericProvider<W, GenericClass<W, Y>>,
                //      Y 
                //  >
                var instantiated = HelperExtensions.InstantiateOpenTemplate_ForTesting(baseType, closedArgs) as GenericInstanceType;

                var arg1 = instantiated.GenericArguments[0] as GenericInstanceType;

                Assert.True(arg1.Open().RefersToSame(provider)); // == OpenGenericProvider<>,
                Assert.AreEqual(W, arg1.GenericArguments[0]); // == <W, ...>

                var nestedGeneric = arg1.GenericArguments[1] as GenericInstanceType;
                Assert.True(nestedGeneric.Open().RefersToSame(module.ImportReference(typeof(GenericClass<,>)))); // == GenericClass<>,
                Assert.AreEqual(W, nestedGeneric.GenericArguments[0]); // == <<W, ...>>
                Assert.AreEqual(Y, nestedGeneric.GenericArguments[1]); // == <<..., Y>>

                Assert.AreEqual(Y, instantiated.GenericArguments[1]); // == <..., Y>>
            }
        }

        [Test]
        public void ClosedParent_WithComplexGenericBaseClass_CanContextuallyBeRecovered_UsingInstantiatedBaseType()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                // NestedGenericExpressionClass`2
                var definition = module.ImportReference(typeof(NestedGenericExpressionClass<,>)).Resolve();
                var provider = module.ImportReference(typeof(OpenGenericProvider<,>));

                // <float, int>
                var closedArgs = new Collection<TypeReference> { module.TypeSystem.Single, module.TypeSystem.Int32 };

                var closed = definition.MakeGenericInstanceType(closedArgs.ToArray());

                var @int = closedArgs[1];
                var @float = closedArgs[0];

                // NestedGenericExpressionClass`2.Q -> float, NestedGenericExpressionClass`2.W -> int
                // GenericClass<OpenGenericProvider<int, GenericClass<int, float>>, float>
                var instantiatedBaseClass = closed.InstantiatedBaseType() as GenericInstanceType;

                var arg1 = instantiatedBaseClass.GenericArguments[0] as GenericInstanceType;

                Assert.True(arg1.Open().RefersToSame(provider)); // == OpenGenericProvider<>,
                Assert.AreEqual(@int, arg1.GenericArguments[0]); // == <int, ...>

                var nestedGeneric = arg1.GenericArguments[1] as GenericInstanceType;
                Assert.True(nestedGeneric.Open().RefersToSame(module.ImportReference(typeof(GenericClass<,>)))); // == GenericClass<>,
                Assert.AreEqual(@int, nestedGeneric.GenericArguments[0]); // == <<int, ...>>
                Assert.AreEqual(@float, nestedGeneric.GenericArguments[1]); // == <<..., float>>

                Assert.AreEqual(@float, instantiatedBaseClass.GenericArguments[1]); // == <..., float>>
            }
        }

        class HalfOpenLeft<T> : GenericClass<T, float> { }
        class HalfOpenRight<T> : GenericClass<float, T> { }

        [TestCase(typeof(HalfOpenLeft<>), 0)]
        [TestCase(typeof(HalfOpenRight<>), 1)]
        public void HalfOpen_BaseClass_CanBeRecovered_UsingInstantiatedBaseType(Type type, int genArgPos)
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;

                var parentType = module.ImportReference(type);
                var halfOpenBaseType = parentType.InstantiatedBaseType() as GenericInstanceType;

                Assert.IsTrue(halfOpenBaseType.Open().RefersToSame(module.ImportReference(typeof(GenericClass<,>))));
                Assert.AreEqual(2, halfOpenBaseType.GenericArguments.Count);
                Assert.IsInstanceOf<GenericParameter>(halfOpenBaseType.GenericArguments[genArgPos]);
                Assert.True(halfOpenBaseType.GenericArguments[1-genArgPos].RefersToSame(module.TypeSystem.Single));
            }
        }

        class Derived<P, Q, R> : GenericClass<float, R> { }
        class Super<T, U, V> : Derived<V, int, U> { }

        [Test]
        public void MultiLevel_GenericClassHierarchy_CanBeRecovered_UsingInstantiatedBaseType()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;

                var parentType = module.ImportReference(typeof(Super<,,>));
                var derivedType = parentType.InstantiatedBaseType() as GenericInstanceType;
                var baseTypeType = derivedType.InstantiatedBaseType() as GenericInstanceType;

                Assert.IsTrue(derivedType.Open().RefersToSame(module.ImportReference(typeof(Derived<,,>))));
                Assert.AreEqual(3, derivedType.GenericArguments.Count);
                Assert.IsInstanceOf<GenericParameter>(derivedType.GenericArguments[0]);
                Assert.True(derivedType.GenericArguments[1].RefersToSame(module.TypeSystem.Int32));
                Assert.IsInstanceOf<GenericParameter>(derivedType.GenericArguments[2]);

                Assert.IsTrue(baseTypeType.Open().RefersToSame(module.ImportReference(typeof(GenericClass<,>))));
                Assert.AreEqual(2, baseTypeType.GenericArguments.Count);
                Assert.True(baseTypeType.GenericArguments[0].RefersToSame(module.TypeSystem.Single));
                Assert.IsInstanceOf<GenericParameter>(baseTypeType.GenericArguments[1]);

                Assert.AreEqual(derivedType.GenericArguments[2], baseTypeType.GenericArguments[1]);
            }
        }

        class OpenGenericParent<T>
        {
#pragma warning disable 693 // CS0693: Type parameter 'T' has the same name as the type parameter from outer type 'UtilityTests.OpenGenericParent<T>'
            public class Open<T> { }
#pragma warning restore 693
            public class Closed { }
        }

        [Test]
        public void EnumeratingInstantiatedNestedTypes_CorrectlyReports_WhetherTheyAreCompletelyClosed()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var parentOpen = module.ImportReference(typeof(OpenGenericParent<>));
                var open = module.ImportReference(typeof(OpenGenericParent<>.Open<>));
                var closed = module.ImportReference(typeof(OpenGenericParent<>.Closed));

                var parentClosed = parentOpen.MakeGenericInstanceType(parentOpen.GenericParameters.ToArray());
                bool checkedOpen = false, checkedClosed = false;

                foreach(var nested in parentClosed.InstantiatedNestedTypes())
                {
                    if (nested.Definition.RefersToSame(open) && (checkedOpen = true))
                        Assert.False(nested.IsCompletelyClosed());
                    else if (nested.Definition.RefersToSame(closed) && (checkedClosed = true))
                        Assert.True(nested.IsCompletelyClosed());
                }

                Assert.True(checkedOpen);
                Assert.True(checkedClosed);
            }
        }

        class OpenGenericParent<Q,S,T>
        {
            public class Nested : GenericClass<T,S> {}
        }

        [Test]
        public void InstantiatedNestedTypes_CorrectlyHandles_GenericParamOrdering()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var parentOpen = module.ImportReference(typeof(OpenGenericParent<,,>));
                var nestedClass = module.ImportReference(typeof(OpenGenericParent<,,>.Nested));
                var baseClass = module.ImportReference(typeof(GenericClass<,>));

                var parentClosed = parentOpen.MakeGenericInstanceType(parentOpen.GenericParameters.ToArray());

                Assert.AreEqual(1, parentClosed.InstantiatedNestedTypes().Count());
                var nested = parentClosed.InstantiatedNestedTypes().First();

                Assert.True(nested.Definition.RefersToSame(nestedClass));

                var @base = (GenericInstanceType)nested.Instantiated.InstantiatedBaseType();
                Assert.True(@base.Open().RefersToSame(baseClass));
                
                Assert.AreEqual(@base.GenericArguments[0], parentClosed.GenericArguments[2]);
                Assert.AreEqual(@base.GenericArguments[1], parentClosed.GenericArguments[1]);
            }
        }

        interface IFaceA { }
        interface IFaceB : IFaceA { }
        interface IFaceC : IFaceB { }

        [Test]
        public void InstantiatedInterfaces_DoesNotReportInterfaces_Twice_AndCanCollectNestedInterfaces()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;

                var ifaceA = module.ImportReference(typeof(IFaceA));
                var ifaceB = module.ImportReference(typeof(IFaceB));
                var ifaceC = module.ImportReference(typeof(IFaceC));

                var set = new List<TypeReference>();
                foreach(var iface in ifaceB.InstantiatedInterfaces())
                {
                    set.ForEach(t => Assert.False(t.RefersToSame(iface.Definition)));
                    set.Add(iface.Definition);
                }

                Assert.AreEqual(1, set.Count);
                Assert.True(set.First().RefersToSame(ifaceA));

                set = new List<TypeReference>();
                foreach (var iface in ifaceC.InstantiatedInterfaces())
                {
                    set.ForEach(t => Assert.False(t.RefersToSame(iface.Definition)));
                    set.Add(iface.Definition);
                }

                Assert.AreEqual(2, set.Count);
                Assert.True(set.Any(i => i.RefersToSame(ifaceA)));
                Assert.True(set.Any(i => i.RefersToSame(ifaceB)));
            }
        }

        class ClassB : IFaceB
        {

        }

        class ClassC : ClassB
        {

        }

        [Test]
        public void InstantiatedInterfaces_TraversesClassHierarchies()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;

                var ifaceA = module.ImportReference(typeof(IFaceA));
                var ifaceB = module.ImportReference(typeof(IFaceB));

                var classB = module.ImportReference(typeof(ClassB));
                var classC = module.ImportReference(typeof(ClassC));

                var set = new List<TypeReference>();
                foreach (var iface in classB.InstantiatedInterfaces())
                {
                    set.ForEach(t => Assert.False(t.RefersToSame(iface.Definition)));
                    set.Add(iface.Definition);
                }

                Assert.AreEqual(2, set.Count);
                Assert.True(set.Any(i => i.RefersToSame(ifaceA)));
                Assert.True(set.Any(i => i.RefersToSame(ifaceB)));

                set = new List<TypeReference>();
                foreach (var iface in classC.InstantiatedInterfaces())
                {
                    set.ForEach(t => Assert.False(t.RefersToSame(iface.Definition)));
                    set.Add(iface.Definition);
                }

                Assert.AreEqual(2, set.Count);
                Assert.True(set.Any(i => i.RefersToSame(ifaceA)));
                Assert.True(set.Any(i => i.RefersToSame(ifaceB)));
            }
        }

        struct Thing<T>
        {
            int IntField;
            double DoubleField;
            T GenericField;
        }

        [Test]
        public void InstantiatedFields_FindsInstantiatedFields()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;
                var floatType = module.TypeSystem.Single;
                var thingOpen = module.ImportReference(typeof(Thing<>));
                var closedParameter = thingOpen.GenericParameters[0].InstantiateOpenTemplate(new Collection<TypeReference> { floatType });

                Assert.True(closedParameter.RefersToSame(floatType));
            }
        }

        [Test]
        public void InstantiateOpenTemplate_CanInstantiateDirectGenericParameter()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var module = cecilAssembly.Assembly.MainModule;

                var intType = module.TypeSystem.Int32;
                var doubleType = module.TypeSystem.Double;

                var floatType = module.TypeSystem.Single;
                var thingOpen = module.ImportReference(typeof(Thing<>));
                var thingClosed = thingOpen.MakeGenericInstanceType(floatType);

                int foundFields = 0;

                foreach (var t in new[] { intType, doubleType, thingOpen.GenericParameters[0] })
                {
                    foreach (var f in thingOpen.InstantiatedFields().ToList())
                    {
                        if (f.SubstitutedType.RefersToSame(t))
                            foundFields++;
                    }
                }

                Assert.AreEqual(3, foundFields);
                foundFields = 0;

                foreach (var t in new[] { intType, doubleType, floatType })
                {
                    foreach (var f in thingClosed.InstantiatedFields().ToList())
                    {
                        if (f.SubstitutedType.RefersToSame(t))
                            foundFields++;
                    }
                }

                Assert.AreEqual(3, foundFields);

            }
        }

    }

    static class DiagErrorExtensions
    {
        public static Diag TestingError(this Diag diag, string message = null)
        {
            diag.Messages.Add(new CompilationPipeline.Common.Diagnostics.DiagnosticMessage()
                {
                    DiagnosticType = CompilationPipeline.Common.Diagnostics.DiagnosticType.Error,
                    MessageData = message
                }
            );
            return diag;
        }

        public static Diag TestingWarning(this Diag diag, string message = null)
        {
            diag.Messages.Add(new CompilationPipeline.Common.Diagnostics.DiagnosticMessage()
                {
                    DiagnosticType = CompilationPipeline.Common.Diagnostics.DiagnosticType.Warning,
                    MessageData = message
                }
            );

            return diag;
        }
    }
}
