/*
    Copyright (C) 2011 de4dot@gmail.com

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

namespace de4dot.renamer {
	abstract class TypeNames {
		protected IDictionary<string, INameCreator> typeNames = new Dictionary<string, INameCreator>(StringComparer.Ordinal);
		protected INameCreator genericParamNameCreator = new NameCreator("gparam_");

		public TypeNames() {
			insertTypeName("System.Boolean", "bool");
			insertTypeName("System.Byte", "byte");
			insertTypeName("System.Char", "char");
			insertTypeName("System.Double", "double");
			insertTypeName("System.Int16", "short");
			insertTypeName("System.Int32", "int");
			insertTypeName("System.Int64", "long");
			insertTypeName("System.IntPtr", "intptr");
			insertTypeName("System.SByte", "sbyte");
			insertTypeName("System.Single", "float");
			insertTypeName("System.String", "string");
			insertTypeName("System.UInt16", "ushort");
			insertTypeName("System.UInt32", "uint");
			insertTypeName("System.UInt64", "ulong");
			insertTypeName("System.UIntPtr", "uintptr");
			insertTypeName("System.Decimal", "decimal");
		}

		public string newName(TypeReference typeRef) {
			var elementType = typeRef.GetElementType();
			if (elementType is GenericParameter)
				return genericParamNameCreator.newName();

			var name = elementType.FullName;
			INameCreator nc;
			if (typeNames.TryGetValue(name, out nc))
				return nc.newName();

			var parts = name.Replace('/', '.').Split(new char[] { '.' });
			var newName = parts[parts.Length - 1];
			int tickIndex = newName.LastIndexOf('`');
			if (tickIndex > 0)
				newName = newName.Substring(0, tickIndex);

			return insertTypeName(name, newName).newName();
		}

		INameCreator insertTypeName(string fullName, string newName) {
			newName = fixName(newName);

			var name2 = " " + newName;
			INameCreator nc;
			if (!typeNames.TryGetValue(name2, out nc))
				typeNames[name2] = nc = new NameCreator(newName + "_");

			typeNames[fullName] = nc;
			return nc;
		}

		protected abstract string fixName(string name);
		public abstract TypeNames clone();

		protected IDictionary<string, INameCreator> cloneDict() {
			var rv = new Dictionary<string, INameCreator>(StringComparer.Ordinal);
			foreach (var key in typeNames.Keys)
				rv[key] = typeNames[key].clone();
			return rv;
		}
	}

	class VariableNameCreator : TypeNames {
		protected override string fixName(string name) {
			// Make all leading upper case chars lower case
			var s = "";
			for (int i = 0; i < name.Length; i++) {
				char c = char.ToLowerInvariant(name[i]);
				if (c == name[i])
					return s + name.Substring(i);
				s += c;
			}
			return s;
		}

		public override TypeNames clone() {
			var rv = new VariableNameCreator();
			rv.typeNames = cloneDict();
			rv.genericParamNameCreator = genericParamNameCreator.clone();
			return rv;
		}
	}

	class PropertyNameCreator : TypeNames {
		protected override string fixName(string name) {
			return name.Substring(0, 1).ToUpperInvariant() + name.Substring(1);
		}

		public override TypeNames clone() {
			var rv = new PropertyNameCreator();
			rv.typeNames = cloneDict();
			rv.genericParamNameCreator = genericParamNameCreator.clone();
			return rv;
		}
	}

	class GlobalInterfacePropertyNameCreator : TypeNames {
		protected override string fixName(string name) {
			return "I_" + name.Substring(0, 1).ToUpperInvariant() + name.Substring(1);
		}

		public override TypeNames clone() {
			return this;
		}
	}
}
