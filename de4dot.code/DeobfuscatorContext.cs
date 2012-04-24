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
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code {
	// "global" data and methods that is shared between all deobfuscators that deobfuscate
	// assemblies at the same time.
	public class DeobfuscatorContext : IDeobfuscatorContext {
		ExternalAssemblies externalAssemblies = new ExternalAssemblies();
		Dictionary<string, object> dataDict = new Dictionary<string, object>(StringComparer.Ordinal);

		public void clear() {
			dataDict.Clear();
			externalAssemblies.unloadAll();
		}

		public void setData(string name, object data) {
			dataDict[name] = data;
		}

		public object getData(string name) {
			object value;
			dataDict.TryGetValue(name, out value);
			return value;
		}

		public void clearData(string name) {
			dataDict.Remove(name);
		}

		static TypeReference getNonGenericTypeReference(TypeReference typeReference) {
			if (typeReference == null)
				return null;
			if (!typeReference.IsGenericInstance)
				return typeReference;
			var type = (GenericInstanceType)typeReference;
			return type.ElementType;
		}

		public TypeDefinition resolve(TypeReference type) {
			if (type == null)
				return null;
			var typeDef = getNonGenericTypeReference(type) as TypeDefinition;
			if (typeDef != null)
				return typeDef;

			return externalAssemblies.resolve(type);
		}

		public MethodDefinition resolve(MethodReference method) {
			if (method == null)
				return null;
			var methodDef = method as MethodDefinition;
			if (methodDef != null)
				return methodDef;

			var type = resolve(method.DeclaringType);
			if (type == null)
				return null;

			foreach (var m in type.Methods) {
				if (MemberReferenceHelper.compareMethodReference(method, m))
					return m;
			}

			return null;
		}

		public FieldDefinition resolve(FieldReference field) {
			if (field == null)
				return null;
			var fieldDef = field as FieldDefinition;
			if (fieldDef != null)
				return fieldDef;

			var type = resolve(field.DeclaringType);
			if (type == null)
				return null;

			foreach (var f in type.Fields) {
				if (MemberReferenceHelper.compareFieldReference(field, f))
					return f;
			}

			return null;
		}
	}
}
