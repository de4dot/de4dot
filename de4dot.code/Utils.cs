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
using System.IO;
using System.Text;

namespace de4dot {
	public enum StartUpArch {
		AnyCpu,
		x86,
		x64,
	}

	// These are in .NET 3.5 and later...
	internal delegate TResult Func<out TResult>();
	internal delegate TResult Func<in T, out TResult>(T arg);
	internal delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2);
	internal delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3);
	internal delegate void Action();
	internal delegate void Action<in T>(T arg);
	internal delegate void Action<in T1, in T2>(T1 arg1, T2 arg2);
	internal delegate void Action<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3);

	class Tuple<T1, T2> {
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

	static class Utils {
		static Random random = new Random();
		public static StartUpArch startUpArch = StartUpArch.AnyCpu;

		public static string getArchString(string anyCpu, string x86, string x64) {
			switch (startUpArch) {
			case StartUpArch.AnyCpu: return anyCpu;
			case StartUpArch.x86: return x86;
			case StartUpArch.x64: return x64;
			default: throw new ApplicationException(string.Format("Invalid startUpArch {0}", startUpArch));
			}
		}

		public static IEnumerable<T> unique<T>(IEnumerable<T> values) {
			// HashSet is only available in .NET 3.5 and later.
			var dict = new Dictionary<T, bool>();
			foreach (var val in values)
				dict[val] = true;
			return dict.Keys;
		}

		public static string toCsharpString(string s) {
			var sb = new StringBuilder(s.Length + 2);
			sb.Append('"');
			foreach (var c in s) {
				if ((int)c < 0x20) {
					switch (c) {
					case '\a': appendEscape(sb, 'a'); break;
					case '\b': appendEscape(sb, 'b'); break;
					case '\f': appendEscape(sb, 'f'); break;
					case '\n': appendEscape(sb, 'n'); break;
					case '\r': appendEscape(sb, 'r'); break;
					case '\t': appendEscape(sb, 't'); break;
					case '\v': appendEscape(sb, 'v'); break;
					default:
						sb.Append(string.Format(@"\u{0:X4}", (int)c));
						break;
					}
				}
				else if (c == '\\' || c == '"') {
					appendEscape(sb, c);
				}
				else
					sb.Append(c);
			}
			sb.Append('"');
			return sb.ToString();
		}

		public static string shellEscape(string s) {
			var sb = new StringBuilder(s.Length + 2);
			sb.Append('"');
			foreach (var c in s) {
				if (c == '"')
					appendEscape(sb, c);
				else
					sb.Append(c);
			}
			sb.Append('"');
			return sb.ToString();
		}

		static void appendEscape(StringBuilder sb, char c) {
			sb.Append('\\');
			sb.Append(c);
		}

		public static string getFullPath(string path) {
			try {
				return Path.GetFullPath(path);
			}
			catch (Exception) {
				return path;
			}
		}

		public static string randomName(int min, int max) {
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

		public static string getBaseName(string name) {
			int index = name.LastIndexOf(Path.DirectorySeparatorChar);
			if (index < 0)
				return name;
			return name.Substring(index + 1);
		}

		public static string getDirName(string name) {
			return Path.GetDirectoryName(name);
		}

		static string ourBaseDir = null;
		public static string getOurBaseDir() {
			if (ourBaseDir != null)
				return ourBaseDir;
			return ourBaseDir = getDirName(getFullPath(Environment.GetCommandLineArgs()[0]));
		}

		public static string getPathOfOurFile(string filename) {
			return Path.Combine(getOurBaseDir(), filename);
		}
	}
}
