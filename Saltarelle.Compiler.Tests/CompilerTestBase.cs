﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using ICSharpCode.NRefactory.TypeSystem;
using NUnit.Framework;
using Saltarelle.Compiler.JSModel;
using Saltarelle.Compiler.JSModel.Expressions;
using Saltarelle.Compiler.JSModel.TypeSystem;

namespace Saltarelle.Compiler.Tests {
    public class CompilerTestBase {
        private class MockSourceFile : ISourceFile {
            private readonly string _fileName;
            private readonly string _content;

            public MockSourceFile(string fileName, string content) {
                _fileName = fileName;
                _content  = content;
            }

            public string FileName {
                get { return _fileName; }
            }

            public TextReader Open() {
                return new StringReader(_content);
            }
        }

        protected class MockNamingConventionResolver : INamingConventionResolver {
            public MockNamingConventionResolver() {
                GetTypeName                               = t => t.Name;
                GetTypeParameterName                      = t => t.Name;
                GetMethodImplementation                   = m => m.IsStatic ? MethodImplOptions.StaticMethod(m.Name) : MethodImplOptions.InstanceMethod(m.Name);
                GetConstructorImplementation              = c => (c.DeclaringType.GetConstructors().Count() == 1 || c.Parameters.Count == 0) ? ConstructorImplOptions.Unnamed() : ConstructorImplOptions.Named("ctor$" + string.Join("$", c.Parameters.Select(p => p.Type.Name)));
                GetPropertyImplementation                 = p => PropertyImplOptions.GetAndSetMethods(MethodImplOptions.InstanceMethod("get_" + p.Name), MethodImplOptions.InstanceMethod("set_" + p.Name));
                GetAutoPropertyBackingFieldImplementation = p => FieldImplOptions.Field("$" + p.Name);
                GetFieldImplementation                    = f => FieldImplOptions.Field("$" + f.Name);
                GetEventImplementation                    = e => e.IsStatic ? EventImplOptions.AddAndRemoveMethods(MethodImplOptions.StaticMethod("add_" + e.Name), MethodImplOptions.StaticMethod("remove_" + e.Name)) : EventImplOptions.AddAndRemoveMethods(MethodImplOptions.InstanceMethod("add_" + e.Name), MethodImplOptions.InstanceMethod("remove_" + e.Name));
                GetAutoEventBackingFieldImplementation    = e => FieldImplOptions.Field("$" + e.Name);
                GetEnumValueName                          = f => "$" + f.Name;
                GetVariableName                           = (v, used) => {
                                                                             string baseName = "$" + v.Name;
                                                                             if (!used.Contains(baseName))
                                                                                 return baseName;
                                                                             int i = 2;
                                                                             while (used.Contains(baseName + i.ToString(CultureInfo.InvariantCulture)))
                                                                                i++;
                                                                             return baseName + i.ToString(CultureInfo.InvariantCulture);
                                                                         };
				GetTemporaryVariableName                  = index => string.Format(CultureInfo.InvariantCulture, "$tmp{0}", index + 1);
            }

            public Func<ITypeDefinition, string> GetTypeName { get; set; }
            public Func<ITypeParameter, string> GetTypeParameterName { get; set; }
            public Func<IMethod, MethodImplOptions> GetMethodImplementation { get; set; }
            public Func<IMethod, ConstructorImplOptions> GetConstructorImplementation { get; set; }
            public Func<IProperty, PropertyImplOptions> GetPropertyImplementation { get; set; }
            public Func<IProperty, FieldImplOptions> GetAutoPropertyBackingFieldImplementation { get; set; }
            public Func<IField, FieldImplOptions> GetFieldImplementation { get; set; }
            public Func<IEvent, EventImplOptions> GetEventImplementation { get; set; }
            public Func<IEvent, FieldImplOptions> GetAutoEventBackingFieldImplementation { get; set; }
            public Func<IField, string> GetEnumValueName { get; set; }
            public Func<IVariable, ISet<string>, string> GetVariableName { get; set; }
			public Func<int, string> GetTemporaryVariableName { get; set; }

            string INamingConventionResolver.GetTypeName(ITypeDefinition typeDefinition) {
                return GetTypeName(typeDefinition);
            }

            string INamingConventionResolver.GetTypeParameterName(ITypeParameter typeDefinition) {
                return GetTypeParameterName(typeDefinition);
            }

            MethodImplOptions INamingConventionResolver.GetMethodImplementation(IMethod method) {
                return GetMethodImplementation(method);
            }

            ConstructorImplOptions INamingConventionResolver.GetConstructorImplementation(IMethod method) {
                return GetConstructorImplementation(method);
            }

            PropertyImplOptions INamingConventionResolver.GetPropertyImplementation(IProperty property) {
                return GetPropertyImplementation(property);
            }

            FieldImplOptions INamingConventionResolver.GetAutoPropertyBackingFieldImplementation(IProperty property) {
                return GetAutoPropertyBackingFieldImplementation(property);
            }

            FieldImplOptions INamingConventionResolver.GetFieldImplementation(IField field) {
                return GetFieldImplementation(field);
            }

            EventImplOptions INamingConventionResolver.GetEventImplementation(IEvent evt) {
                return GetEventImplementation(evt);
            }

            FieldImplOptions INamingConventionResolver.GetAutoEventBackingFieldImplementation(IEvent evt) {
                return GetAutoEventBackingFieldImplementation(evt);
            }

            string INamingConventionResolver.GetEnumValueName(IField value) {
                return GetEnumValueName(value);
            }

            string INamingConventionResolver.GetVariableName(IVariable variable, ISet<string> usedNames) {
                return GetVariableName(variable, usedNames);
            }

			string INamingConventionResolver.GetTemporaryVariableName(int index) {
				return GetTemporaryVariableName(index);
			}
        }

		public class MockRuntimeLibrary : IRuntimeLibrary {
			public MockRuntimeLibrary() {
				TypeIs                      = (c, e, t) => JsExpression.Invocation(JsExpression.Identifier("$TypeIs"), e, t);
				TryCast                     = (c, e, t) => JsExpression.Invocation(JsExpression.Identifier("$TryCast"), e, t);
				Cast                        = (c, e, t) => JsExpression.Invocation(JsExpression.Identifier("$Cast"), e, t);
				ImplicitReferenceConversion = (c, e, t) => JsExpression.Invocation(JsExpression.Identifier("$Upcast"), e, t);
				InstantiateGenericType      = (c, e, a) => JsExpression.Invocation(JsExpression.Identifier("$MkGeneric"), new[] { e }.Concat(a));
				MakeException               = (c, e)    => JsExpression.Invocation(JsExpression.Identifier("$MakeException"), e);
				IntegerDivision             = (c, n, d) => JsExpression.Invocation(JsExpression.Identifier("$IntDiv"), n, d);
				Coalesce                    = (c, a, b) => JsExpression.Invocation(JsExpression.Identifier("$Coalesce"), a, b);
				Lift                        = (c, e)    => JsExpression.Invocation(JsExpression.Identifier("$Lift"), e);
			}

			public Func<ICompilation, JsExpression, JsExpression, JsExpression> TypeIs { get; set; }
			public Func<ICompilation, JsExpression, JsExpression, JsExpression> TryCast { get; set; }
			public Func<ICompilation, JsExpression, JsExpression, JsExpression> Cast { get; set; }
			public Func<ICompilation, JsExpression, IEnumerable<JsExpression>, JsExpression> InstantiateGenericType { get; set; }
			public Func<ICompilation, JsExpression, JsExpression, JsExpression> ImplicitReferenceConversion { get; set; }
			public Func<ICompilation, JsExpression, JsExpression> MakeException { get; set; }
			public Func<ICompilation, JsExpression, JsExpression, JsExpression> IntegerDivision { get; set; }
			public Func<ICompilation, JsExpression, JsExpression, JsExpression> Coalesce { get; set; }
			public Func<ICompilation, JsExpression, JsExpression> Lift { get; set; }
			
			JsExpression IRuntimeLibrary.TypeIs(ICompilation compilation, JsExpression expression, JsExpression targetType) {
				return TypeIs(compilation, expression, targetType);
			}

			JsExpression IRuntimeLibrary.TryCast(ICompilation compilation, JsExpression expression, JsExpression targetType) {
				return TryCast(compilation, expression, targetType);
			}

			JsExpression IRuntimeLibrary.Cast(ICompilation compilation, JsExpression expression, JsExpression targetType) {
				return Cast(compilation, expression, targetType);
			}

			JsExpression IRuntimeLibrary.ImplicitReferenceConversion(ICompilation compilation, JsExpression expression, JsExpression targetType) {
				return ImplicitReferenceConversion(compilation, expression, targetType);
			}

			JsExpression IRuntimeLibrary.InstantiateGenericType(ICompilation compilation, JsExpression type, IEnumerable<JsExpression> typeArguments) {
				return InstantiateGenericType(compilation, type, typeArguments);
			}

			JsExpression IRuntimeLibrary.MakeException(ICompilation compilation, JsExpression operand) {
				return MakeException(compilation, operand);
			}

			JsExpression IRuntimeLibrary.IntegerDivision(ICompilation compilation, JsExpression numerator, JsExpression denominator) {
				return IntegerDivision(compilation, numerator, denominator);
			}

			JsExpression IRuntimeLibrary.Coalesce(ICompilation compilation, JsExpression numerator, JsExpression denominator) {
				return Coalesce(compilation, numerator, denominator);
			}

			JsExpression IRuntimeLibrary.Lift(ICompilation compilation, JsExpression expression) {
				return Lift(compilation, expression);
			}
		}

        protected class MockErrorReporter : IErrorReporter {
        	private readonly bool _logToConsole;
        	public List<string> AllMessages { get; set; }

            public MockErrorReporter(bool logToConsole) {
            	_logToConsole = logToConsole;
            	AllMessages = new List<string>();
                Error   = s => { s = "Error: " + s; if (logToConsole) Console.WriteLine(s); AllMessages.Add(s); };
                Warning = s => { s = "Warning: " + s; if (logToConsole) Console.WriteLine(s); AllMessages.Add(s); };
            }

            public Action<string> Error { get; set; }
            public Action<string> Warning { get; set; }

            void IErrorReporter.Error(string message) {
                Error(message);
            }

            void IErrorReporter.Warning(string message) {
                Warning(message);
            }
        }

        private static readonly Lazy<IAssemblyReference> _mscorlibLazy = new Lazy<IAssemblyReference>(() => new CecilLoader().LoadAssemblyFile(typeof(object).Assembly.Location));
        protected IAssemblyReference Mscorlib { get { return _mscorlibLazy.Value; } }

        protected ReadOnlyCollection<JsType> CompiledTypes { get; private set; }

        protected void Compile(IEnumerable<string> sources, INamingConventionResolver namingConvention = null, IRuntimeLibrary runtimeLibrary = null, IErrorReporter errorReporter = null, Action<IMethod, JsFunctionDefinitionExpression, MethodCompiler> methodCompiled = null) {
            var sourceFiles = sources.Select((s, i) => new MockSourceFile("File" + i + ".cs", s)).ToList();
            bool defaultErrorHandling = false;
            if (errorReporter == null) {
                defaultErrorHandling = true;
                errorReporter = new MockErrorReporter(true);
            }

            var compiler = new Compiler(namingConvention ?? new MockNamingConventionResolver(), runtimeLibrary ?? new MockRuntimeLibrary(), errorReporter);
            if (methodCompiled != null)
                compiler.MethodCompiled += methodCompiled;

            CompiledTypes = compiler.Compile(sourceFiles, new[] { Mscorlib }).AsReadOnly();
            if (defaultErrorHandling) {
                ((MockErrorReporter)errorReporter).AllMessages.Should().BeEmpty("Compile should not generate errors");
            }
        }

        protected void Compile(params string[] sources) {
            Compile((IEnumerable<string>)sources);
        }

        protected string Stringify(JsExpression expression) {
            switch (expression.NodeType) {
                case ExpressionNodeType.Identifier: return ((JsIdentifierExpression)expression).Name;
                case ExpressionNodeType.TypeReference: return "{" + ((JsTypeReferenceExpression)expression).TypeDefinition.ReflectionName + "}";
                default: throw new ArgumentException("expression");
            }
        }

        protected string Stringify(JsConstructedType tp) {
            return Stringify(tp.UnboundType) + (tp.TypeArguments.Count > 0 ? "<" + string.Join(",", tp.TypeArguments.Select(x => Stringify(x))) + ">" : "");
        }

        protected JsClass FindClass(string name) {
            var result = CompiledTypes.SingleOrDefault(t => t.Name.ToString() == name);
            if (result == null) Assert.Fail("Could not find type " + name);
            if (!(result is JsClass)) Assert.Fail("Found type is not a JsClass, it is a " + result.GetType().Name);
            return (JsClass)result;
        }

        protected JsEnum FindEnum(string name) {
            var result = CompiledTypes.SingleOrDefault(t => t.Name.ToString() == name);
            if (result == null) Assert.Fail("Could not find type " + name);
            if (!(result is JsEnum)) Assert.Fail("Found type is not a JsEnum, it is a " + result.GetType().Name);
            return (JsEnum)result;
        }

        protected JsMethod FindInstanceMethod(string name) {
            var lastDot = name.LastIndexOf('.');
            var cls = FindClass(name.Substring(0, lastDot));
            return cls.InstanceMethods.SingleOrDefault(m => m.Name == name.Substring(lastDot + 1));
        }

        protected JsMethod FindStaticMethod(string name) {
            var lastDot = name.LastIndexOf('.');
            var cls = FindClass(name.Substring(0, lastDot));
            return cls.StaticMethods.SingleOrDefault(m => m.Name == name.Substring(lastDot + 1));
        }

        protected JsConstructor FindConstructor(string name) {
            var lastDot = name.LastIndexOf('.');
            var cls = FindClass(name.Substring(0, lastDot));
            return cls.Constructors.SingleOrDefault(m => (m.Name ?? "<default>") == name.Substring(lastDot + 1));
        }

        protected JsField FindInstanceField(string name) {
            var lastDot = name.LastIndexOf('.');
            var cls = FindClass(name.Substring(0, lastDot));
            return cls.InstanceFields.SingleOrDefault(f => f.Name == name.Substring(lastDot + 1));
        }

        protected JsField FindStaticField(string name) {
            var lastDot = name.LastIndexOf('.');
            var cls = FindClass(name.Substring(0, lastDot));
            return cls.StaticFields.SingleOrDefault(f => f.Name == name.Substring(lastDot + 1));
        }

        protected JsEnumValue FindEnumValue(string name) {
            var lastDot = name.LastIndexOf('.');
            var cls = FindEnum(name.Substring(0, lastDot));
            return cls.Values.SingleOrDefault(f => f.Name == name.Substring(lastDot + 1));
        }
    }
}
