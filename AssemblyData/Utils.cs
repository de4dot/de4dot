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
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace AssemblyData {
	internal delegate void Action<T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
	internal delegate void Action<T1, T2, T3, T4, T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
	internal delegate void Action<T1, T2, T3, T4, T5, T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, T17 arg17);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, T17 arg17, T18 arg18);
	internal delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, T17 arg17, T18 arg18, T19 arg19);
	internal delegate TResult Func<T1, T2, T3, T4, T5, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, T17 arg17);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, T17 arg17, T18 arg18);
	internal delegate TResult Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16, T17 arg17, T18 arg18, T19 arg19);

	static class Utils {
		static Random random = new Random();

		public static uint GetRandomUint() => (uint)(random.NextDouble() * uint.MaxValue);

		public static Type GetDelegateType(Type returnType, Type[] args) {
			Type[] types;
			if (returnType == typeof(void)) {
				types = args;
				switch (types.Length) {
				case 0: return typeof(Action).MakeGenericType(types);
				case 1: return typeof(Action<>).MakeGenericType(types);
				case 2: return typeof(Action<,>).MakeGenericType(types);
				case 3: return typeof(Action<,,>).MakeGenericType(types);
				case 4: return typeof(Action<,,,>).MakeGenericType(types);
				case 5: return typeof(Action<,,,,>).MakeGenericType(types);
				case 6: return typeof(Action<,,,,,>).MakeGenericType(types);
				case 7: return typeof(Action<,,,,,,>).MakeGenericType(types);
				case 8: return typeof(Action<,,,,,,,>).MakeGenericType(types);
				case 9: return typeof(Action<,,,,,,,,>).MakeGenericType(types);
				case 10: return typeof(Action<,,,,,,,,,>).MakeGenericType(types);
				case 11: return typeof(Action<,,,,,,,,,,>).MakeGenericType(types);
				case 12: return typeof(Action<,,,,,,,,,,,>).MakeGenericType(types);
				case 13: return typeof(Action<,,,,,,,,,,,,>).MakeGenericType(types);
				case 14: return typeof(Action<,,,,,,,,,,,,,>).MakeGenericType(types);
				case 15: return typeof(Action<,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 16: return typeof(Action<,,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 17: return typeof(Action<,,,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 18: return typeof(Action<,,,,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 19: return typeof(Action<,,,,,,,,,,,,,,,,,,>).MakeGenericType(types);
				default:
					throw new ApplicationException($"Too many delegate type arguments: {types.Length}");
				}
			}
			else {
				types = new Type[args.Length + 1];
				Array.Copy(args, types, args.Length);
				types[types.Length - 1] = returnType;

				switch (types.Length) {
				case 1: return typeof(Func<>).MakeGenericType(types);
				case 2: return typeof(Func<,>).MakeGenericType(types);
				case 3: return typeof(Func<,,>).MakeGenericType(types);
				case 4: return typeof(Func<,,,>).MakeGenericType(types);
				case 5: return typeof(Func<,,,,>).MakeGenericType(types);
				case 6: return typeof(Func<,,,,,>).MakeGenericType(types);
				case 7: return typeof(Func<,,,,,,>).MakeGenericType(types);
				case 8: return typeof(Func<,,,,,,,>).MakeGenericType(types);
				case 9: return typeof(Func<,,,,,,,,>).MakeGenericType(types);
				case 10: return typeof(Func<,,,,,,,,,>).MakeGenericType(types);
				case 11: return typeof(Func<,,,,,,,,,,>).MakeGenericType(types);
				case 12: return typeof(Func<,,,,,,,,,,,>).MakeGenericType(types);
				case 13: return typeof(Func<,,,,,,,,,,,,>).MakeGenericType(types);
				case 14: return typeof(Func<,,,,,,,,,,,,,>).MakeGenericType(types);
				case 15: return typeof(Func<,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 16: return typeof(Func<,,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 17: return typeof(Func<,,,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 18: return typeof(Func<,,,,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 19: return typeof(Func<,,,,,,,,,,,,,,,,,,>).MakeGenericType(types);
				case 20: return typeof(Func<,,,,,,,,,,,,,,,,,,,>).MakeGenericType(types);
				default:
					throw new ApplicationException($"Too many delegate type arguments: {types.Length}");
				}
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

		public static void AddCallStringDecrypterMethodInstructions(MethodInfo method, ILGenerator ilg) {
			var args = method.GetParameters();
			for (int i = 0; i < args.Length; i++) {
				var arg = args[i].ParameterType;

				ilg.Emit(OpCodes.Ldarg_0);
				ilg.Emit(OpCodes.Ldc_I4, i);
				ilg.Emit(OpCodes.Ldelem_Ref);

				if (arg.IsValueType)
					ilg.Emit(OpCodes.Unbox_Any, arg);
				else
					ilg.Emit(OpCodes.Castclass, arg);
			}
			ilg.Emit(OpCodes.Call, method);
			ilg.Emit(OpCodes.Ret);
		}

		public static string GetFullPath(string path) {
			try {
				return Path.GetFullPath(path);
			}
			catch (Exception) {
				return path;
			}
		}

		public static string GetDirName(string name) => Path.GetDirectoryName(name);
	}
}
