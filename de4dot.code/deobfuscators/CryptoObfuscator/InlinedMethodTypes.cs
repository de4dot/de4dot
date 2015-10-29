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

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class InlinedMethodTypes {
		Dictionary<TypeDef, TypeFlags> types = new Dictionary<TypeDef, TypeFlags>();

		[Flags]
		enum TypeFlags {
			DontRemoveType = 1,
		}

		public IEnumerable<TypeDef> Types {
			get {
				foreach (var kv in types) {
					if ((kv.Value & TypeFlags.DontRemoveType) == 0)
						yield return kv.Key;
				}
			}
		}

		static bool IsValidType(TypeDef type) {
			if (type == null)
				return false;

			if (type.BaseType == null || type.BaseType.FullName != "System.Object")
				return false;
			if (type.DeclaringType != null)
				return false;
			if (type.Attributes != (TypeAttributes.NotPublic | TypeAttributes.AutoLayout |
				TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.AnsiClass))
				return false;
			if (type.HasProperties || type.HasEvents)
				return false;
			if (type.HasInterfaces)
				return false;
			if (type.HasGenericParameters)
				return false;
			if (type.HasNestedTypes)
				return false;

			return true;
		}

		public static bool IsValidMethodType(TypeDef type) {
			if (!IsValidType(type))
				return false;

			if (type.HasFields)
				return false;
			if (type.Methods.Count != 1)
				return false;

			return true;
		}

		public static bool IsValidFieldType(TypeDef type) {
			if (!IsValidType(type))
				return false;

			if (type.HasMethods)
				return false;
			if (type.Fields.Count != 1)
				return false;

			return true;
		}

		public void Add(TypeDef type) {
			if (type == null || types.ContainsKey(type))
				return;
			types[type] = 0;
		}

		public void DontRemoveType(TypeDef type) {
			TypeFlags flags;
			types.TryGetValue(type, out flags);
			flags |= TypeFlags.DontRemoveType;
			types[type] = flags;
		}
	}
}
