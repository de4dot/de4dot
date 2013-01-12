/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using de4dot.blocks;

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
			initTypes();
		}

		void initTypes() {
			foreach (var type in assembly.GetTypes()) {
				string key = (type.Namespace ?? "") + "." + type.Name;
				List<TypeResolver> list;
				if (!types.TryGetValue(key, out list))
					types[key] = list = new List<TypeResolver>();
				list.Add(new TypeResolver(type));
			}
		}

		TypeResolver getTypeResolver(ITypeDefOrRef typeRef) {
			if (typeRef == null)
				return null;
			var scopeType = typeRef.ScopeType;
			var key = scopeType.Namespace + "." + scopeType.TypeName;
			List<TypeResolver> list;
			if (!types.TryGetValue(key, out list))
				return null;

			if (scopeType is TypeDef) {
				foreach (var resolver in list) {
					if (resolver.type.MetadataToken == scopeType.MDToken.Raw)
						return resolver;
				}
			}

			foreach (var resolver in list) {
				if (ResolverUtils.compareTypes(resolver.type, scopeType))
					return resolver;
			}

			return null;
		}

		public FieldInfo resolve(IField fieldRef) {
			var resolver = getTypeResolver(fieldRef.DeclaringType);
			if (resolver != null)
				return resolver.resolve(fieldRef);
			return resolveGlobalField(fieldRef);
		}

		FieldInfo resolveGlobalField(IField fieldRef) {
			initGlobalFields();
			foreach (var globalField in globalFields) {
				if (ResolverUtils.compareFields(globalField, fieldRef))
					return globalField;
			}
			return null;
		}

		void initGlobalFields() {
			if (globalFields != null)
				return;
			globalFields = new List<FieldInfo>();

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
			foreach (var module in assembly.GetModules(true)) {
				foreach (var method in module.GetFields(flags))
					globalFields.Add(method);
			}
		}

		public MethodBase resolve(IMethod methodRef) {
			var resolver = getTypeResolver(methodRef.DeclaringType);
			if (resolver != null)
				return resolver.resolve(methodRef);
			return resolveGlobalMethod(methodRef);
		}

		MethodBase resolveGlobalMethod(IMethod methodRef) {
			initGlobalMethods();
			foreach (var globalMethod in globalMethods) {
				if (ResolverUtils.compareMethods(globalMethod, methodRef))
					return globalMethod;
			}
			return null;
		}

		void initGlobalMethods() {
			if (globalMethods != null)
				return;
			globalMethods = new List<MethodBase>();

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
			foreach (var module in assembly.GetModules(true)) {
				foreach (var method in module.GetMethods(flags))
					globalMethods.Add(method);
			}
		}

		public Type resolve(ITypeDefOrRef typeRef) {
			var resolver = getTypeResolver(typeRef);
			if (resolver != null)
				return resolver.type;

			var ts = typeRef as TypeSpec;
			if (ts != null && ts.TypeSig is GenericSig)
				return typeof(MGenericParameter);

			return null;
		}

		public override string ToString() {
			return assembly.ToString();
		}
	}
}
