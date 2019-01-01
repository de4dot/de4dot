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
using System.Text;

namespace AssemblyData {
	// This class will make sure no data in the string is destroyed by serialization
	[Serializable]
	class MyString {
		short[] data;

		public MyString() {
		}

		public MyString(string s) {
			if (s == null)
				data = null;
			else {
				data = new short[s.Length];
				for (int i = 0; i < s.Length; i++)
					data[i] = (short)s[i];
			}
		}

		public override string ToString() {
			if (data == null)
				return null;

			var sb = new StringBuilder(data.Length);
			foreach (var c in data)
				sb.Append((char)c);
			return sb.ToString();
		}
	}

	public static class SimpleData {
		public static object[] Pack(object[] args) {
			for (int i = 0; i < args.Length; i++) {
				if (args[i] is string s)
					args[i] = new MyString(s);
			}
			return args;
		}

		public static object[] Unpack(object[] args) {
			for (int i = 0; i < args.Length; i++) {
				if (args[i] is MyString s)
					args[i] = s.ToString();
			}
			return args;
		}
	}
}
