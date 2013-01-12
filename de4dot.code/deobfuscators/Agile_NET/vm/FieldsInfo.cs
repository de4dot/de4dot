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
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.Agile_NET.vm {
	class FieldsInfo {
		public static readonly object EnumType = new object();
		Dictionary<string, int> fieldTypes = new Dictionary<string, int>(StringComparer.Ordinal);
		int numEnums = 0;

		public FieldsInfo(TypeDef type)
			: this(type.Fields) {
		}

		public FieldsInfo(IEnumerable<FieldDef> fields) {
			foreach (var field in fields) {
				var fieldTypeDef = field.FieldSig.GetFieldType().TryGetTypeDef();
				if (fieldTypeDef != null && fieldTypeDef.IsEnum)
					addEnum();
				else
					add(field.FieldSig.GetFieldType());
			}
		}

		public FieldsInfo(object[] fieldTypes) {
			foreach (var o in fieldTypes) {
				if (o == EnumType)
					addEnum();
				else
					add((string)o);
			}
		}

		void add(TypeSig type) {
			add(type.GetFullName());
		}

		void add(string typeFullName) {
			int count;
			fieldTypes.TryGetValue(typeFullName, out count);
			fieldTypes[typeFullName] = count + 1;
		}

		void addEnum() {
			numEnums++;
		}

		public bool isSame(FieldsInfo other) {
			if (numEnums != other.numEnums)
				return false;
			if (fieldTypes.Count != other.fieldTypes.Count)
				return false;
			foreach (var kv in fieldTypes) {
				int num;
				if (!other.fieldTypes.TryGetValue(kv.Key, out num))
					return false;
				if (kv.Value != num)
					return false;
			}
			return true;
		}
	}
}
