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

using dnlib.DotNet;

namespace de4dot.code.deobfuscators.Agile_NET {
	class StringDecrypterInfo {
		public readonly MethodDef Method;
		public readonly FieldDef Field;

		public StringDecrypterInfo(MethodDef method)
			: this(method, null) {
		}

		public StringDecrypterInfo(MethodDef method, FieldDef field) {
			Method = method;
			Field = field;
		}

		public override int GetHashCode() {
			int hash = 0;
			if (Method != null)
				hash ^= Method.GetHashCode();
			if (Field != null)
				hash ^= Field.GetHashCode();
			return hash;
		}

		public override bool Equals(object obj) {
			var other = obj as StringDecrypterInfo;
			return other != null &&
				Method == other.Method &&
				Field == other.Field;
		}
	}
}
