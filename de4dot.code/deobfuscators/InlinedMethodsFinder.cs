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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	public static class InlinedMethodsFinder {
		public static List<MethodDef> Find(ModuleDef module) {
			// Not all garbage methods are inlined, possibly because we remove some code that calls
			// the garbage method before the methods inliner has a chance to inline it. Try to find
			// all garbage methods and other code will figure out if there are any calls left.

			var inlinedMethods = new List<MethodDef>();
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (!method.IsStatic)
						continue;
					if (!method.IsAssembly && !method.IsPrivateScope && !method.IsPrivate)
						continue;
					if (method.GenericParameters.Count > 0)
						continue;
					if (method.Name == ".cctor")
						continue;
					if (method.Body == null)
						continue;
					var instrs = method.Body.Instructions;
					if (instrs.Count < 2)
						continue;

					switch (instrs[0].OpCode.Code) {
					case Code.Ldc_I4:
					case Code.Ldc_I4_0:
					case Code.Ldc_I4_1:
					case Code.Ldc_I4_2:
					case Code.Ldc_I4_3:
					case Code.Ldc_I4_4:
					case Code.Ldc_I4_5:
					case Code.Ldc_I4_6:
					case Code.Ldc_I4_7:
					case Code.Ldc_I4_8:
					case Code.Ldc_I4_M1:
					case Code.Ldc_I4_S:
					case Code.Ldc_I8:
					case Code.Ldc_R4:
					case Code.Ldc_R8:
					case Code.Ldftn:
					case Code.Ldnull:
					case Code.Ldstr:
					case Code.Ldtoken:
					case Code.Ldsfld:
					case Code.Ldsflda:
						if (instrs[1].OpCode.Code != Code.Ret)
							continue;
						break;

					case Code.Ldarg:
					case Code.Ldarg_S:
					case Code.Ldarg_0:
					case Code.Ldarg_1:
					case Code.Ldarg_2:
					case Code.Ldarg_3:
					case Code.Ldarga:
					case Code.Ldarga_S:
					case Code.Call:
					case Code.Newobj:
						if (!IsCallMethod(method))
							continue;
						break;

					default:
						continue;
					}

					inlinedMethods.Add(method);
				}
			}

			return inlinedMethods;
		}

		static bool IsCallMethod(MethodDef method) {
			int loadIndex = 0;
			int methodArgsCount = DotNetUtils.GetArgsCount(method);
			var instrs = method.Body.Instructions;
			int i = 0;
			for (; i < instrs.Count && i < methodArgsCount; i++) {
				var instr = instrs[i];
				switch (instr.OpCode.Code) {
				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
				case Code.Ldarga:
				case Code.Ldarga_S:
					if (instr.GetParameterIndex() != loadIndex)
						return false;
					loadIndex++;
					continue;
				}
				break;
			}
			if (loadIndex != methodArgsCount)
				return false;
			if (i + 1 >= instrs.Count)
				return false;

			switch (instrs[i].OpCode.Code) {
			case Code.Call:
			case Code.Callvirt:
			case Code.Newobj:
			case Code.Ldfld:
			case Code.Ldflda:
			case Code.Ldftn:
			case Code.Ldvirtftn:
				break;
			default:
				return false;
			}
			if (instrs[i + 1].OpCode.Code != Code.Ret)
				return false;

			return true;
		}
	}
}
