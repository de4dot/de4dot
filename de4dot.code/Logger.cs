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

namespace de4dot.code {
	public class Logger : ILogger {
		public readonly static Logger Instance = new Logger();

		int indentLevel = 0;
		readonly int indentSize = 0;
		LoggerEvent maxLoggerEvent = LoggerEvent.Info;
		string indentString = "";
		Dictionary<string, bool> ignoredMessages = new Dictionary<string, bool>(StringComparer.Ordinal);
		int numIgnoredMessages;
		bool canIgnoreMessages;

		public int IndentLevel {
			get { return indentLevel; }
			set {
				if (indentLevel == value)
					return;
				indentLevel = value;
				InitIndentString();
			}
		}

		public LoggerEvent MaxLoggerEvent {
			get { return maxLoggerEvent; }
			set { maxLoggerEvent = value; }
		}

		public bool CanIgnoreMessages {
			get { return canIgnoreMessages; }
			set { canIgnoreMessages = value; }
		}

		public int NumIgnoredMessages {
			get { return numIgnoredMessages; }
		}

		public Logger()
			: this(2, true) {
		}

		public Logger(int indentSize, bool canIgnoreMessages) {
			this.indentSize = indentSize;
			this.canIgnoreMessages = canIgnoreMessages;
		}

		void InitIndentString() {
			if (indentLevel < 0)
				indentLevel = 0;
			indentString = new string(' ', indentLevel * indentSize);
		}

		public void Indent() {
			indentLevel++;
			InitIndentString();
		}

		public void DeIndent() {
			indentLevel--;
			InitIndentString();
		}

		public void Log(object sender, LoggerEvent loggerEvent, string format, params object[] args) {
			Log(true, sender, loggerEvent, format, args);
		}

		public void LogErrorDontIgnore(string format, params object[] args) {
			Log(false, null, LoggerEvent.Error, format, args);
		}

		public void Log(bool canIgnore, object sender, LoggerEvent loggerEvent, string format, params object[] args) {
			if (IgnoresEvent(loggerEvent))
				return;
			if (canIgnore && IgnoreMessage(loggerEvent, format, args))
				return;

			switch (loggerEvent) {
			case LoggerEvent.Error:
				foreach (var l in string.Format(format, args).Split('\n'))
					LogMessage(string.Empty, string.Format("ERROR: {0}", l));
				break;

			case LoggerEvent.Warning:
				foreach (var l in string.Format(format, args).Split('\n'))
					LogMessage(string.Empty, string.Format("WARNING: {0}", l));
				break;

			default:
				var indent = loggerEvent <= LoggerEvent.Warning ? "" : indentString;
				LogMessage(indent, format, args);
				break;
			}
		}

		bool IgnoreMessage(LoggerEvent loggerEvent, string format, object[] args) {
			if (loggerEvent != LoggerEvent.Error && loggerEvent != LoggerEvent.Warning)
				return false;
			if (!canIgnoreMessages)
				return false;
			if (ignoredMessages.ContainsKey(format)) {
				numIgnoredMessages++;
				return true;
			}
			ignoredMessages[format] = true;
			return false;
		}

		void LogMessage(string indent, string format, params object[] args) {
			if (args == null || args.Length == 0)
				Console.WriteLine("{0}{1}", indent, format);
			else
				Console.WriteLine(indent + format, args);
		}

		public bool IgnoresEvent(LoggerEvent loggerEvent) {
			return loggerEvent > maxLoggerEvent;
		}

		public static void Log(LoggerEvent loggerEvent, string format, params object[] args) {
			Instance.Log(null, loggerEvent, format, args);
		}

		public static void e(string format, params object[] args) {
			Instance.Log(null, LoggerEvent.Error, format, args);
		}

		public static void w(string format, params object[] args) {
			Instance.Log(null, LoggerEvent.Warning, format, args);
		}

		public static void n(string format, params object[] args) {
			Instance.Log(null, LoggerEvent.Info, format, args);
		}

		public static void v(string format, params object[] args) {
			Instance.Log(null, LoggerEvent.Verbose, format, args);
		}

		public static void vv(string format, params object[] args) {
			Instance.Log(null, LoggerEvent.VeryVerbose, format, args);
		}
	}
}
