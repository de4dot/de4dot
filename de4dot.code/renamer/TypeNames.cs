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

namespace de4dot.code.renamer {
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

			NameCreator nc;
			var typeFullName = typeRef.FullName;
			if (typeNames.TryGetValue(typeFullName, out nc))
				return nc.create();

			var name = elementType.FullName;
			var parts = name.Replace('/', '.').Split(new char[] { '.' });
			var newName = parts[parts.Length - 1];
			int tickIndex = newName.LastIndexOf('`');
			if (tickIndex > 0)
				newName = newName.Substring(0, tickIndex);

			return addTypeName(typeFullName, newName, prefix).create();
		}

		static string getPrefix(TypeReference typeRef) {
			string prefix = "";
			while (typeRef is PointerType) {
				typeRef = ((PointerType)typeRef).ElementType;
				prefix += "p";
			}
			return prefix;
		}

		protected INameCreator addTypeName(string fullName, string newName, string prefix) {
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
		public VariableNameCreator(bool init = true) {
			if (!init)
				return;
			initTypeName("System.Boolean", "bool");
			initTypeName("System.Byte", "byte");
			initTypeName("System.Char", "char");
			initTypeName("System.Double", "double");
			initTypeName("System.Int16", "short");
			initTypeName("System.Int32", "int");
			initTypeName("System.Int64", "long");
			initTypeName("System.IntPtr", "intptr", "IntPtr");
			initTypeName("System.SByte", "sbyte", "SByte");
			initTypeName("System.Single", "float");
			initTypeName("System.String", "string");
			initTypeName("System.UInt16", "ushort", "UShort");
			initTypeName("System.UInt32", "uint", "UInt");
			initTypeName("System.UInt64", "ulong", "ULong");
			initTypeName("System.UIntPtr", "uintptr", "UIntPtr");
			initTypeName("System.Decimal", "decimal");
		}

		void initTypeName(string fullName, string newName, string ptrName = null) {
			if (ptrName == null)
				ptrName = upperFirst(newName);
			initTypeName2(fullName, "", newName);
			initTypeName2(fullName + "[]", "", newName);
			initTypeName2(fullName + "[][]", "", newName);
			initTypeName2(fullName + "[][][]", "", newName);
			initTypeName2(fullName + "[0...,0...]", "", newName);
			initTypeName2(fullName + "*", "p", ptrName);
			initTypeName2(fullName + "**", "pp", ptrName);
		}

		void initTypeName2(string fullName, string prefix, string newName) {
			addTypeName(fullName, newName, prefix);
			addTypeName(fullName + "&", newName, prefix);
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
			return prefix.ToUpperInvariant() + upperFirst(name);
		}
	}
}
