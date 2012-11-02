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
using dot10.DotNet;
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

		static ITypeDefOrRef getNonGenericTypeReference(ITypeDefOrRef typeRef) {
			var ts = typeRef as TypeSpec;
			if (ts == null)
				return typeRef;
			var gis = ts.TypeSig.RemovePinnedAndModifiers() as GenericInstSig;
			if (gis == null || gis.GenericType == null)
				return typeRef;
			return gis.GenericType.TypeDefOrRef;
		}

		public TypeDef resolveType(ITypeDefOrRef type) {
			if (type == null)
				return null;
			type = getNonGenericTypeReference(type);

			var typeDef = type as TypeDef;
			if (typeDef != null)
				return typeDef;

			var tr = type as TypeRef;
			if (tr != null)
				return tr.Resolve();

			return null;
		}

		public MethodDef resolveMethod(MemberRef method) {
			if (method == null)
				return null;

			var type = resolveType(method.DeclaringType);
			if (type == null)
				return null;

			return type.Resolve(method) as MethodDef;
		}

		public FieldDef resolveField(MemberRef field) {
			if (field == null)
				return null;

			var type = resolveType(field.DeclaringType);
			if (type == null)
				return null;

			return type.Resolve(field) as FieldDef;
		}
	}
}
