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
using System.Reflection;
using System.Diagnostics;

namespace de4dot.blocks {
	public class StackTracePatcher {
		static readonly FieldInfo methodField;
		static readonly FieldInfo framesField;
		static readonly FieldInfo methodsToSkipField;

		static StackTracePatcher() {
			methodField = GetStackFrameMethodField();
			framesField = GetStackTraceStackFramesField();
			methodsToSkipField = GetMethodsToSkipField();
		}

		static FieldInfo GetStackFrameMethodField() {
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return GetFieldThrow(typeof(StackFrame), typeof(MethodBase), flags, "Could not find StackFrame's method (MethodBase) field");
		}

		static FieldInfo GetStackTraceStackFramesField() {
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return GetFieldThrow(typeof(StackTrace), typeof(StackFrame[]), flags, "Could not find StackTrace's frames (StackFrame[]) field");
		}

		static FieldInfo GetMethodsToSkipField() {
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return GetFieldThrow(typeof(StackTrace), "m_iMethodsToSkip", flags, "Could not find StackTrace's iMethodsToSkip field");
		}

		static FieldInfo GetFieldThrow(Type type, Type fieldType, BindingFlags flags, string msg) {
			var info = GetField(type, fieldType, flags);
			if (info != null)
				return info;
			throw new ApplicationException(msg);
		}

		static FieldInfo GetField(Type type, Type fieldType, BindingFlags flags) {
			foreach (var field in type.GetFields(flags)) {
				if (field.FieldType == fieldType)
					return field;
			}
			return null;
		}

		static FieldInfo GetFieldThrow(Type type, string fieldName, BindingFlags flags, string msg) {
			var info = GetField(type, fieldName, flags);
			if (info != null)
				return info;
			throw new ApplicationException(msg);
		}

		static FieldInfo GetField(Type type, string fieldName, BindingFlags flags) {
			foreach (var field in type.GetFields(flags)) {
				if (field.Name == fieldName)
					return field;
			}
			return null;
		}

		public static StackTrace WriteStackFrame(StackTrace stackTrace, int frameNo, MethodBase newMethod) {
			var frames = (StackFrame[])framesField.GetValue(stackTrace);
			int numFramesToSkip = (int)methodsToSkipField.GetValue(stackTrace);
			WriteMethodBase(frames[numFramesToSkip + frameNo], newMethod);
			return stackTrace;
		}

		static void WriteMethodBase(StackFrame frame, MethodBase method) {
			methodField.SetValue(frame, method);
			if (frame.GetMethod() != method)
				throw new ApplicationException(string.Format("Could not set new method: {0}", method));
		}
	}
}
