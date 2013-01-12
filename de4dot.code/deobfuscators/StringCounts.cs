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
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators {
	class StringCounts {
		Dictionary<string, int> strings = new Dictionary<string, int>(StringComparer.Ordinal);

		public IEnumerable<string> Strings {
			get { return strings.Keys; }
		}

		public int NumStrings {
			get { return strings.Count; }
		}

		public void add(string s) {
			int count;
			strings.TryGetValue(s, out count);
			strings[s] = count + 1;
		}

		public bool exists(string s) {
			if (s == null)
				return false;
			return strings.ContainsKey(s);
		}

		public bool all(IList<string> list) {
			foreach (var s in list) {
				if (!exists(s))
					return false;
			}
			return true;
		}

		public bool exactly(IList<string> list) {
			return list.Count == strings.Count && all(list);
		}

		public int count(string s) {
			int count;
			strings.TryGetValue(s, out count);
			return count;
		}
	}

	class FieldTypes : StringCounts {
		public FieldTypes(TypeDef type) {
			init(type.Fields);
		}

		public FieldTypes(IEnumerable<FieldDef> fields) {
			init(fields);
		}

		void init(IEnumerable<FieldDef> fields) {
			if (fields == null)
				return;
			foreach (var field in fields) {
				var type = field.FieldSig.GetFieldType();
				if (type != null)
					add(type.FullName);
			}
		}
	}

	class LocalTypes : StringCounts {
		public LocalTypes(MethodDef method) {
			if (method != null && method.Body != null)
				init(method.Body.Variables);
		}

		public LocalTypes(IEnumerable<Local> locals) {
			init(locals);
		}

		void init(IEnumerable<Local> locals) {
			if (locals == null)
				return;
			foreach (var local in locals)
				add(local.Type.FullName);
		}
	}
}
