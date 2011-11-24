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
		protected Dictionary<string, NameCreator> typeNames = new Dictionary<string, NameCreator>(StringComparer.Ordinal);
		protected NameCreator genericParamNameCreator = new NameCreator("gparam_");

		public string create(TypeReference typeRef) {
			if (typeRef.IsGenericInstance) {
				var git = (GenericInstanceType)typeRef;
				if (git.ElementType.FullName == "System.Nullable`1" &&
					git.GenericArguments.Count == 1 && git.GenericArguments[0] != null) {
					typeRef = git.GenericArguments[0];
				}
			}

			string prefix = getPrefix(typeRef);

			var elementType = typeRef.GetElementType();
			if (elementType is GenericParameter)
				return genericParamNameCreator.create();

			var name = elementType.FullName;
			NameCreator nc;
			if (typeNames.TryGetValue(name, out nc))
				return nc.create();

			var parts = name.Replace('/', '.').Split(new char[] { '.' });
			var newName = parts[parts.Length - 1];
			int tickIndex = newName.LastIndexOf('`');
			if (tickIndex > 0)
				newName = newName.Substring(0, tickIndex);

			return addTypeName(name, newName, prefix).create();
		}

		string getPrefix(TypeReference typeRef) {
			string prefix = "";
			while (typeRef is PointerType) {
				typeRef = ((PointerType)typeRef).ElementType;
				prefix += "p";
			}
			return prefix;
		}

		protected INameCreator addTypeName(string fullName, string newName, string prefix = "") {
			newName = fixName(prefix, newName);

			var name2 = " " + newName;
			NameCreator nc;
			if (!typeNames.TryGetValue(name2, out nc))
				typeNames[name2] = nc = new NameCreator(newName + "_");

			typeNames[fullName] = nc;
			return nc;
		}

		protected abstract string fixName(string prefix, string name);

		public virtual TypeNames merge(TypeNames other) {
			foreach (var pair in other.typeNames) {
				if (typeNames.ContainsKey(pair.Key))
					typeNames[pair.Key].merge(pair.Value);
				else
					typeNames[pair.Key] = pair.Value.clone();
			}
			genericParamNameCreator.merge(other.genericParamNameCreator);
			return this;
		}

		protected static string upperFirst(string s) {
			return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1);
		}
	}

	class VariableNameCreator : TypeNames {
		public VariableNameCreator() {
			addTypeName("System.Boolean", "bool");
			addTypeName("System.Byte", "byte");
			addTypeName("System.Char", "char");
			addTypeName("System.Double", "double");
			addTypeName("System.Int16", "short");
			addTypeName("System.Int32", "int");
			addTypeName("System.Int64", "long");
			addTypeName("System.IntPtr", "intptr");
			addTypeName("System.SByte", "sbyte");
			addTypeName("System.Single", "float");
			addTypeName("System.String", "string");
			addTypeName("System.UInt16", "ushort");
			addTypeName("System.UInt32", "uint");
			addTypeName("System.UInt64", "ulong");
			addTypeName("System.UIntPtr", "uintptr");
			addTypeName("System.Decimal", "decimal");
		}

		static string lowerLeadingChars(string name) {
			var s = "";
			for (int i = 0; i < name.Length; i++) {
				char c = char.ToLowerInvariant(name[i]);
				if (c == name[i])
					return s + name.Substring(i);
				s += c;
			}
			return s;
		}

		protected override string fixName(string prefix, string name) {
			name = lowerLeadingChars(name);
			if (prefix == "")
				return name;
			return prefix + upperFirst(name);
		}
	}

	class PropertyNameCreator : TypeNames {
		protected override string fixName(string prefix, string name) {
			return prefix + upperFirst(name);
		}
	}
}
