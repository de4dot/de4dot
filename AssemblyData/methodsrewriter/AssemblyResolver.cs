/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using Mono.Cecil;
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

		TypeResolver getTypeResolver(TypeReference typeReference) {
			var key = typeReference.Namespace + "." + typeReference.Name;
			List<TypeResolver> list;
			if (!types.TryGetValue(key, out list))
				return null;

			if (typeReference is TypeDefinition) {
				foreach (var resolver in list) {
					if (resolver.type.MetadataToken == typeReference.MetadataToken.ToInt32())
						return resolver;
				}
			}

			foreach (var resolver in list) {
				if (ResolverUtils.compareTypes(resolver.type, typeReference))
					return resolver;
			}

			return null;
		}

		public FieldInfo resolve(FieldReference fieldReference) {
			var resolver = getTypeResolver(fieldReference.DeclaringType);
			if (resolver != null)
				return resolver.resolve(fieldReference);
			return resolveGlobalField(fieldReference);
		}

		FieldInfo resolveGlobalField(FieldReference fieldReference) {
			initGlobalFields();
			foreach (var globalField in globalFields) {
				if (ResolverUtils.compareFields(globalField, fieldReference))
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

		public MethodBase resolve(MethodReference methodReference) {
			var resolver = getTypeResolver(methodReference.DeclaringType);
			if (resolver != null)
				return resolver.resolve(methodReference);
			return resolveGlobalMethod(methodReference);
		}

		MethodBase resolveGlobalMethod(MethodReference methodReference) {
			initGlobalMethods();
			foreach (var globalMethod in globalMethods) {
				if (ResolverUtils.compareMethods(globalMethod, methodReference))
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

		public Type resolve(TypeReference typeReference) {
			var resolver = getTypeResolver(typeReference);
			if (resolver != null)
				return resolver.type;

			if (typeReference.IsGenericParameter)
				return typeof(MGenericParameter);

			return null;
		}

		public override string ToString() {
			return assembly.ToString();
		}
	}
}
