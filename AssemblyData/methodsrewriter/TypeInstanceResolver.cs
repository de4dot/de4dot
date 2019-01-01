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
using de4dot.blocks;

namespace AssemblyData.methodsrewriter {
	class TypeInstanceResolver {
		Type type;
		Dictionary<string, List<MethodBase>> methods;
		Dictionary<string, List<FieldInfo>> fields;

		public TypeInstanceResolver(Type type, ITypeDefOrRef typeRef) => this.type = ResolverUtils.MakeInstanceType(type, typeRef);

		public FieldInfo Resolve(IField fieldRef) {
			InitFields();

			if (!fields.TryGetValue(fieldRef.Name.String, out var list))
				return null;

			fieldRef = GenericArgsSubstitutor.Create(fieldRef, fieldRef.DeclaringType.TryGetGenericInstSig());

			foreach (var field in list) {
				if (ResolverUtils.CompareFields(field, fieldRef))
					return field;
			}

			return null;
		}

		void InitFields() {
			if (fields != null)
				return;
			fields = new Dictionary<string, List<FieldInfo>>(StringComparer.Ordinal);

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
			foreach (var field in type.GetFields(flags)) {
				if (!fields.TryGetValue(field.Name, out var list))
					fields[field.Name] = list = new List<FieldInfo>();
				list.Add(field);
			}
		}

		public MethodBase Resolve(IMethod methodRef) {
			InitMethods();

			if (!methods.TryGetValue(methodRef.Name.String, out var list))
				return null;

			methodRef = GenericArgsSubstitutor.Create(methodRef, methodRef.DeclaringType.TryGetGenericInstSig());

			foreach (var method in list) {
				if (ResolverUtils.CompareMethods(method, methodRef))
					return method;
			}

			return null;
		}

		void InitMethods() {
			if (methods != null)
				return;
			methods = new Dictionary<string, List<MethodBase>>(StringComparer.Ordinal);

			var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
			foreach (var method in ResolverUtils.GetMethodBases(type, flags)) {
				if (!methods.TryGetValue(method.Name, out var list))
					methods[method.Name] = list = new List<MethodBase>();
				list.Add(method);
			}
		}
	}
}
