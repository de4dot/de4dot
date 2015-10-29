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

namespace de4dot.code {
	public static class Win32Path {
		public static string GetFileName(string path) {
			if (path == null)
				return null;
			if (path.Length == 0)
				return string.Empty;
			var c = path[path.Length - 1];
			if (c == '\\' || c == ':')
				return string.Empty;
			int index = path.LastIndexOf('\\');
			if (index < 0)
				return path;
			return path.Substring(index + 1);
		}

		public static string GetFileNameWithoutExtension(string path) {
			if (path == null)
				return null;
			var s = GetFileName(path);
			int i = s.LastIndexOf('.');
			if (i < 0)
				return s;
			return s.Substring(0, i);
		}

		public static string GetExtension(string path) {
			if (path == null)
				return null;
			var s = GetFileName(path);
			int i = s.LastIndexOf('.');
			if (i < 0)
				return string.Empty;
			return s.Substring(i);
		}
	}
}
