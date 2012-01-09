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

namespace de4dot.code {
	public static class Log {
		public static int indentLevel = 0;
		const int indentSize = 2;

		public enum LogLevel {
			error,
			warning,
			normal,
			verbose,
			veryverbose,
		}
		public static LogLevel logLevel = LogLevel.normal;
		public static string indentString = "";

		public static bool isAtLeast(LogLevel ll) {
			return logLevel >= ll;
		}

		static void initIndentString() {
			indentString = new string(' ', indentLevel * indentSize);
		}

		public static void indent() {
			indentLevel++;
			initIndentString();
		}

		public static void deIndent() {
			if (indentLevel <= 0)
				throw new ApplicationException("Can't de-indent!");
			indentLevel--;
			initIndentString();
		}

		public static void log(LogLevel l, string format, params object[] args) {
			if (!isAtLeast(l))
				return;
			var indent = l <= LogLevel.warning ? "" : indentString;
			Console.WriteLine(indent + format, args);
		}

		public static void e(string format, params object[] args) {
			log(LogLevel.error, format, args);
		}

		public static void w(string format, params object[] args) {
			log(LogLevel.warning, format, args);
		}

		public static void n(string format, params object[] args) {
			log(LogLevel.normal, format, args);
		}

		public static void v(string format, params object[] args) {
			log(LogLevel.verbose, format, args);
		}

		public static void vv(string format, params object[] args) {
			log(LogLevel.veryverbose, format, args);
		}
	}
}
