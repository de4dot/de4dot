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
using dnlib.DotNet;

namespace de4dot.code.renamer {
	public abstract class TypeNames {
		protected Dictionary<string, NameCreator> typeNames = new Dictionary<string, NameCreator>(StringComparer.Ordinal);
		protected NameCreator genericParamNameCreator = new NameCreator("gparam_");
		protected NameCreator fnPtrNameCreator = new NameCreator("fnptr_");
		protected NameCreator unknownNameCreator = new NameCreator("unknown_");
		protected Dictionary<string, string> fullNameToShortName;
		protected Dictionary<string, string> fullNameToShortNamePrefix;

		public string Create(TypeSig typeRef) {
			typeRef = typeRef.RemovePinnedAndModifiers();
			if (typeRef == null)
				return unknownNameCreator.Create();
			if (typeRef is GenericInstSig gis) {
				if (gis.FullName == "System.Nullable`1" &&
					gis.GenericArguments.Count == 1 && gis.GenericArguments[0] != null) {
					typeRef = gis.GenericArguments[0];
				}
			}

			string prefix = GetPrefix(typeRef);

			var elementType = Renamer.GetScopeType(typeRef);
			if (elementType == null && IsFnPtrSig(typeRef))
				return fnPtrNameCreator.Create();
			if (IsGenericParam(elementType))
				return genericParamNameCreator.Create();

			var typeFullName = typeRef.FullName;
			if (typeNames.TryGetValue(typeFullName, out var nc))
				return nc.Create();

			var fullName = elementType == null ? typeRef.FullName : elementType.FullName;
			var dict = prefix == "" ? fullNameToShortName : fullNameToShortNamePrefix;
			if (!dict.TryGetValue(fullName, out string shortName)) {
				fullName = fullName.Replace('/', '.');
				int index = fullName.LastIndexOf('.');
				shortName = index > 0 ? fullName.Substring(index + 1) : fullName;

				index = shortName.LastIndexOf('`');
				if (index > 0)
					shortName = shortName.Substring(0, index);
			}

			return AddTypeName(typeFullName, shortName, prefix).Create();
		}

		bool IsFnPtrSig(TypeSig sig) {
			while (sig != null) {
				if (sig is FnPtrSig)
					return true;
				sig = sig.Next;
			}
			return false;
		}

		bool IsGenericParam(ITypeDefOrRef tdr) {
			var ts = tdr as TypeSpec;
			if (ts == null)
				return false;
			var sig = ts.TypeSig.RemovePinnedAndModifiers();
			return sig is GenericSig;
		}

		static string GetPrefix(TypeSig typeRef) {
			string prefix = "";
			while (typeRef != null) {
				if (typeRef.IsPointer)
					prefix += "p";
				typeRef = typeRef.Next;
			}
			return prefix;
		}

		protected INameCreator AddTypeName(string fullName, string newName, string prefix) {
			newName = FixName(prefix, newName);

			var name2 = " " + newName;
			if (!typeNames.TryGetValue(name2, out var nc))
				typeNames[name2] = nc = new NameCreator(newName + "_");

			typeNames[fullName] = nc;
			return nc;
		}

		protected abstract string FixName(string prefix, string name);

		public virtual TypeNames Merge(TypeNames other) {
			if (this == other)
				return this;
			foreach (var pair in other.typeNames) {
				if (typeNames.TryGetValue(pair.Key, out var nc))
					nc.Merge(pair.Value);
				else
					typeNames[pair.Key] = pair.Value.Clone();
			}
			genericParamNameCreator.Merge(other.genericParamNameCreator);
			fnPtrNameCreator.Merge(other.fnPtrNameCreator);
			unknownNameCreator.Merge(other.unknownNameCreator);
			return this;
		}

		protected static string UpperFirst(string s) {
			if (string.IsNullOrEmpty(s))
				return string.Empty;
			return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1);
		}
	}

	public class VariableNameCreator : TypeNames {
		static Dictionary<string, string> ourFullNameToShortName;
		static Dictionary<string, string> ourFullNameToShortNamePrefix;
		static VariableNameCreator() {
			ourFullNameToShortName = new Dictionary<string, string>(StringComparer.Ordinal) {
				{ "System.Boolean", "bool" },
				{ "System.Byte", "byte" },
				{ "System.Char", "char" },
				{ "System.Double", "double" },
				{ "System.Int16", "short" },
				{ "System.Int32", "int" },
				{ "System.Int64", "long" },
				{ "System.IntPtr", "intptr" },
				{ "System.SByte", "sbyte" },
				{ "System.Single", "float" },
				{ "System.String", "string" },
				{ "System.UInt16", "ushort" },
				{ "System.UInt32", "uint" },
				{ "System.UInt64", "ulong" },
				{ "System.UIntPtr", "uintptr" },
				{ "System.Decimal", "decimal" },
			};
			ourFullNameToShortNamePrefix = new Dictionary<string, string>(StringComparer.Ordinal) {
				{ "System.Boolean", "Bool" },
				{ "System.Byte", "Byte" },
				{ "System.Char", "Char" },
				{ "System.Double", "Double" },
				{ "System.Int16", "Short" },
				{ "System.Int32", "Int" },
				{ "System.Int64", "Long" },
				{ "System.IntPtr", "IntPtr" },
				{ "System.SByte", "SByte" },
				{ "System.Single", "Float" },
				{ "System.String", "String" },
				{ "System.UInt16", "UShort" },
				{ "System.UInt32", "UInt" },
				{ "System.UInt64", "ULong" },
				{ "System.UIntPtr", "UIntPtr" },
				{ "System.Decimal", "Decimal" },
			};
		}

		public VariableNameCreator() {
			fullNameToShortName = ourFullNameToShortName;
			fullNameToShortNamePrefix = ourFullNameToShortNamePrefix;
		}

		static string LowerLeadingChars(string name) {
			var s = "";
			for (int i = 0; i < name.Length; i++) {
				char c = char.ToLowerInvariant(name[i]);
				if (c == name[i])
					return s + name.Substring(i);
				s += c;
			}
			return s;
		}

		protected override string FixName(string prefix, string name) {
			name = LowerLeadingChars(name);
			if (prefix == "")
				return name;
			return prefix + UpperFirst(name);
		}
	}

	public class PropertyNameCreator : TypeNames {
		static Dictionary<string, string> ourFullNameToShortName = new Dictionary<string, string>(StringComparer.Ordinal);
		static Dictionary<string, string> ourFullNameToShortNamePrefix = new Dictionary<string, string>(StringComparer.Ordinal);

		public PropertyNameCreator() {
			fullNameToShortName = ourFullNameToShortName;
			fullNameToShortNamePrefix = ourFullNameToShortNamePrefix;
		}

		protected override string FixName(string prefix, string name) => prefix.ToUpperInvariant() + UpperFirst(name);
	}
}
