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
using System.IO;
using System.Text;
using dnlib.DotNet;
using dnlib.IO;

namespace de4dot.code {
	// These are in .NET 3.5 and later...
	public delegate TResult Func<TResult>();
	public delegate TResult Func<T, TResult>(T arg);
	public delegate TResult Func<T1, T2, TResult>(T1 arg1, T2 arg2);
	public delegate TResult Func<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3);
	public delegate void Action();
	public delegate void Action<T>(T arg);
	public delegate void Action<T1, T2>(T1 arg1, T2 arg2);
	public delegate void Action<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3);

	public class Tuple<T1, T2> {
		public T1 Item1 { get; set; }
		public T2 Item2 { get; set; }
		public override bool Equals(object obj) {
			var other = obj as Tuple<T1, T2>;
			if (other == null)
				return false;
			return Item1.Equals(other.Item1) && Item2.Equals(other.Item2);
		}
		public override int GetHashCode() {
			return Item1.GetHashCode() + Item2.GetHashCode();
		}
		public override string ToString() {
			return "<" + Item1.ToString() + "," + Item2.ToString() + ">";
		}
	}

	public static class Utils {
		static Random random = new Random();

		public static IEnumerable<T> Unique<T>(IEnumerable<T> values) {
			// HashSet is only available in .NET 3.5 and later.
			var dict = new Dictionary<T, bool>();
			foreach (var val in values)
				dict[val] = true;
			return dict.Keys;
		}

		public static string ToCsharpString(UTF8String s) {
			return ToCsharpString(UTF8String.ToSystemStringOrEmpty(s));
		}

		public static string ToCsharpString(string s) {
			var sb = new StringBuilder(s.Length + 2);
			sb.Append('"');
			foreach (var c in s) {
				if ((int)c < 0x20) {
					switch (c) {
					case '\a': AppendEscape(sb, 'a'); break;
					case '\b': AppendEscape(sb, 'b'); break;
					case '\f': AppendEscape(sb, 'f'); break;
					case '\n': AppendEscape(sb, 'n'); break;
					case '\r': AppendEscape(sb, 'r'); break;
					case '\t': AppendEscape(sb, 't'); break;
					case '\v': AppendEscape(sb, 'v'); break;
					default:
						sb.Append(string.Format(@"\u{0:X4}", (int)c));
						break;
					}
				}
				else if (c == '\\' || c == '"') {
					AppendEscape(sb, c);
				}
				else
					sb.Append(c);
			}
			sb.Append('"');
			return sb.ToString();
		}

		public static string ShellEscape(string s) {
			var sb = new StringBuilder(s.Length + 2);
			sb.Append('"');
			foreach (var c in s) {
				if (c == '"')
					AppendEscape(sb, c);
				else
					sb.Append(c);
			}
			sb.Append('"');
			return sb.ToString();
		}

		static void AppendEscape(StringBuilder sb, char c) {
			sb.Append('\\');
			sb.Append(c);
		}

		public static string RemoveNewlines(object o) {
			return RemoveNewlines(o.ToString());
		}

		public static string RemoveNewlines(string s) {
			return s.Replace('\n', ' ').Replace('\r', ' ');
		}

		public static string GetFullPath(string path) {
			try {
				return Path.GetFullPath(path);
			}
			catch (Exception) {
				return path;
			}
		}

		public static string RandomName(int min, int max) {
			int numChars = random.Next(min, max + 1);
			var sb = new StringBuilder(numChars);
			int numLower = 0;
			for (int i = 0; i < numChars; i++) {
				if (numLower == 0)
					sb.Append((char)((int)'A' + random.Next(26)));
				else
					sb.Append((char)((int)'a' + random.Next(26)));

				if (numLower == 0) {
					numLower = random.Next(1, 5);
				}
				else {
					numLower--;
				}
			}
			return sb.ToString();
		}

		public static string GetBaseName(string name) {
			int index = name.LastIndexOf(Path.DirectorySeparatorChar);
			if (index < 0)
				return name;
			return name.Substring(index + 1);
		}

		public static string GetDirName(string name) {
			return Path.GetDirectoryName(name);
		}

		static string ourBaseDir = null;
		public static string GetOurBaseDir() {
			if (ourBaseDir != null)
				return ourBaseDir;
			return ourBaseDir = GetDirName(typeof(Utils).Assembly.Location);
		}

		public static string GetPathOfOurFile(string filename) {
			return Path.Combine(GetOurBaseDir(), filename);
		}

		// This fixes a mono (tested 2.10.5) String.StartsWith() bug. NB: stringComparison must be
		// Ordinal or OrdinalIgnoreCase!
		public static bool StartsWith(string left, string right, StringComparison stringComparison) {
			if (left.Length < right.Length)
				return false;
			return left.Substring(0, right.Length).Equals(right, stringComparison);
		}

		public static string GetAssemblySimpleName(string name) {
			int i = name.IndexOf(',');
			if (i < 0)
				return name;
			return name.Substring(0, i);
		}

		public static bool PathExists(string path) {
			try {
				return new DirectoryInfo(path).Exists;
			}
			catch (Exception) {
				return false;
			}
		}

		public static bool FileExists(string path) {
			try {
				return new FileInfo(path).Exists;
			}
			catch (Exception) {
				return false;
			}
		}

		public static bool Compare(byte[] a, byte[] b) {
			if (a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++) {
				if (a[i] != b[i])
					return false;
			}
			return true;
		}

		public static byte[] ReadFile(string filename) {
			// If the file is on the network, and we read more than 2MB, we'll read from the wrong
			// offset in the file! Tested: VMware 8, Win7 x64.
			const int MAX_BYTES_READ = 0x200000;

			using (var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				var fileData = new byte[(int)fileStream.Length];

				int bytes, offset = 0, length = fileData.Length;
				while ((bytes = fileStream.Read(fileData, offset, Math.Min(MAX_BYTES_READ, length - offset))) > 0)
					offset += bytes;
				if (offset != length)
					throw new ApplicationException("Could not read all bytes");

				return fileData;
			}
		}
	}
}
