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
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace AssemblyData {
	internal delegate void Action();
	internal delegate TResult Func<out TResult>();
	internal delegate TResult Func<in T1, out TResult>(T1 arg);
	internal delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2);
	internal delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3);
	internal delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
	internal delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
	internal delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
	internal delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
	internal delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
	internal delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9);

	static class Utils {
		static Random random = new Random();

		public static Type getDelegateType(MethodInfo method) {
			var parameters = method.GetParameters();
			var types = new Type[parameters.Length + 1];
			for (int i = 0; i < parameters.Length; i++)
				types[i] = parameters[i].ParameterType;
			types[types.Length - 1] = method.ReturnType;

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
			case 10:return typeof(Func<,,,,,,,,,>).MakeGenericType(types);
			default:
				throw new ApplicationException(string.Format("Too many arguments: {0}", method));
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

		public static void addCallStringDecrypterMethodInstructions(MethodInfo method, ILGenerator ilg) {
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
	}
}
