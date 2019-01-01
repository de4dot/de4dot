/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using dnlib.DotNet;

namespace AssemblyData.methodsrewriter {
	class MGenericParameter {
	}

	class AssemblyResolver {
		Dictionary<string, List<TypeResolver>> types = new Dictionary<string, List<TypeResolver>>(StringComparer.Ordinal);
		List<MethodBase> globalMethods;
		List<FieldInfo> globalFields;
		Assembly assembly;

		public AssemblyResolver(string asmName) {
			assembly = Assembly.Load(new AssemblyName(asmName));
			InitTypes();
		}

		void InitTypes() {
			foreach (var type in assembly.GetTypes()) {
				string key = (type.Namespace ?? "") + "." + type.Name;
				if (!types.TryGetValue(key, out var list))
					types[key] = list = new List<TypeResolver>();
				list.Add(new TypeResolver(type));
			}
		}

		TypeResolver GetTypeResolver(ITypeDefOrRef typeRef) {
			if (typeRef == null)
				return null;
			var scopeType = typeRef.ScopeType;
			var key = scopeType.Namespace + "." + scopeType.TypeName;
			if (!types.TryGetValue(key, out var list))
				return null;

			if (scopeType is TypeDef) {
				foreach (var resolver in list) {
					if (resolver.type.MetadataToken == scopeType.MDToken.Raw)
						return resolver;
				}
			}

			foreach (var resolver in list) {
				if (ResolverUtils.CompareTypes(resolver.type, scopeType))
					return resolver;
			}

			return null;
		}

		public FieldInfo Resolve(IField fieldRef) {
			var resolver = GetTypeResolver(fieldRef.DeclaringType);
			if (resolver != null)
				return resolver.Resolve(fieldRef);
			return ResolveGlobalField(fieldRef);
		}

		FieldInfo ResolveGlobalField(IField fieldRef) {
			InitGlobalFields();
			foreach (var globalField in globalFields) {
				if (ResolverUtils.CompareFields(globalField, fieldRef))
					return globalField;
			}
			return null;
		}

		void InitGlobalFields() {
			if (globalFields != null)
				return;
			globalFields = new List<FieldInfo>();

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
			foreach (var module in assembly.GetModules(true)) {
				foreach (var method in module.GetFields(flags))
					globalFields.Add(method);
			}
		}

		public MethodBase Resolve(IMethod methodRef) {
			var resolver = GetTypeResolver(methodRef.DeclaringType);
			if (resolver != null)
				return resolver.Resolve(methodRef);
			return ResolveGlobalMethod(methodRef);
		}

		MethodBase ResolveGlobalMethod(IMethod methodRef) {
			InitGlobalMethods();
			foreach (var globalMethod in globalMethods) {
				if (ResolverUtils.CompareMethods(globalMethod, methodRef))
					return globalMethod;
			}
			return null;
		}

		void InitGlobalMethods() {
			if (globalMethods != null)
				return;
			globalMethods = new List<MethodBase>();

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
			foreach (var module in assembly.GetModules(true)) {
				foreach (var method in module.GetMethods(flags))
					globalMethods.Add(method);
			}
		}

		public Type Resolve(ITypeDefOrRef typeRef) {
			var resolver = GetTypeResolver(typeRef);
			if (resolver != null)
				return resolver.type;

			if (typeRef is TypeSpec ts && ts.TypeSig is GenericSig)
				return typeof(MGenericParameter);

			return null;
		}

		public override string ToString() => assembly.ToString();
	}
}
