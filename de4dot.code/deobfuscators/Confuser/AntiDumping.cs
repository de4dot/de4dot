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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class AntiDumping : IVersionProvider {
		ModuleDefMD module;
		MethodDef initMethod;
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v14_r58564,
			v14_r58852,
			v16_r69339,
			v17_r74708,
			v18_r75257,
			v19_r75725,
			v19_r76186,
		}

		public MethodDef InitMethod => initMethod;
		public TypeDef Type => initMethod?.DeclaringType;
		public bool Detected => initMethod != null;
		public AntiDumping(ModuleDefMD module) => this.module = module;

		public void Find(ISimpleDeobfuscator simpleDeobfuscator) {
			if (CheckMethod(simpleDeobfuscator, DotNetUtils.GetModuleTypeCctor(module)))
				return;
		}

		bool CheckMethod(ISimpleDeobfuscator simpleDeobfuscator, MethodDef method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (calledMethod == null || !calledMethod.IsStatic)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "()"))
					continue;
				var type = calledMethod.DeclaringType;
				if (type.NestedTypes.Count > 0)
					continue;

				simpleDeobfuscator.Deobfuscate(calledMethod, SimpleDeobfuscatorFlags.Force | SimpleDeobfuscatorFlags.DisableConstantsFolderExtraInstrs);
				if (CheckType(type, calledMethod)) {
					initMethod = calledMethod;
					return true;
				}
			}
			return false;
		}

		bool CheckType(TypeDef type, MethodDef initMethod) =>
			CheckType_v14_r58564(type, initMethod) ||
			CheckType_v14_r58852(type, initMethod);

		bool CheckType_v14_r58564(TypeDef type, MethodDef initMethod) {
			var virtualProtect = DotNetUtils.GetPInvokeMethod(type, "VirtualProtect");
			if (virtualProtect == null)
				return false;
			if (!DotNetUtils.CallsMethod(initMethod, "System.IntPtr System.Runtime.InteropServices.Marshal::GetHINSTANCE(System.Reflection.Module)"))
				return false;
			if (ConfuserUtils.CountCalls(initMethod, virtualProtect) != 3)
				return false;
			if (!DeobUtils.HasInteger(initMethod, 224))
				return false;
			if (!DeobUtils.HasInteger(initMethod, 240))
				return false;
			if (!DeobUtils.HasInteger(initMethod, 267))
				return false;

			version = ConfuserVersion.v14_r58564;
			return true;
		}

		bool CheckType_v14_r58852(TypeDef type, MethodDef initMethod) {
			var virtualProtect = DotNetUtils.GetPInvokeMethod(type, "VirtualProtect");
			if (virtualProtect == null)
				return false;
			if (!DotNetUtils.CallsMethod(initMethod, "System.IntPtr System.Runtime.InteropServices.Marshal::GetHINSTANCE(System.Reflection.Module)"))
				return false;
			int virtualProtectCalls = ConfuserUtils.CountCalls(initMethod, virtualProtect);
			if (virtualProtectCalls != 14 && virtualProtectCalls != 16)
				return false;
			if (!DeobUtils.HasInteger(initMethod, 0x3C))
				return false;
			if (!DeobUtils.HasInteger(initMethod, 0x6c64746e))
				return false;
			if (!DeobUtils.HasInteger(initMethod, 0x6c642e6c))
				return false;
			if (!DeobUtils.HasInteger(initMethod, 0x6f43744e))
				return false;
			if (!DeobUtils.HasInteger(initMethod, 0x6e69746e))
				return false;
			int locallocs = ConfuserUtils.CountOpCode(initMethod, Code.Localloc);

			if (DeobUtils.HasInteger(initMethod, 0x18))
				version = ConfuserVersion.v14_r58852;
			else if (virtualProtectCalls == 16)
				version = ConfuserVersion.v16_r69339;
			else if (virtualProtectCalls == 14) {
				if (locallocs == 2)
					version = ConfuserVersion.v17_r74708;
				else if (locallocs == 1) {
					if (DotNetUtils.HasString(initMethod, "<Unknown>"))
						version = ConfuserVersion.v18_r75257;
					else if (IsRev75725(initMethod))
						version = ConfuserVersion.v19_r75725;
					else
						version = ConfuserVersion.v19_r76186;
				}
				else
					return false;
			}
			else
				return false;

			return true;
		}

		static bool IsRev75725(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 9; i++) {
				if (!instrs[i].IsLdcI4() || instrs[i].GetLdcI4Value() != 8)
					continue;
				if (!instrs[i + 1].IsLdcI4() || instrs[i + 1].GetLdcI4Value() != 64)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Ldloca && instrs[i + 2].OpCode.Code != Code.Ldloca_S)
					continue;
				var call = instrs[i + 3];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || calledMethod.ImplMap == null || calledMethod.ImplMap.Name != "VirtualProtect")
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Pop)
					continue;

				var ldloc = instrs[i + 5];
				if (!ldloc.IsLdloc())
					continue;
				var local = ldloc.GetLocal(method.Body.Variables);
				if (local == null)
					continue;

				if (!instrs[i + 6].IsLdcI4() || instrs[i + 6].GetLdcI4Value() != 0)
					continue;
				if (instrs[i + 7].OpCode.Code != Code.Stind_I4)
					continue;

				ldloc = instrs[i + 8];
				if (!ldloc.IsLdloc() || local != ldloc.GetLocal(method.Body.Variables))
					continue;
				if (!instrs[i + 9].IsLdcI4() || instrs[i + 9].GetLdcI4Value() != 4)
					continue;

				return true;
			}
			return false;
		}

		public bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v14_r58564:
				minRev = 58564;
				maxRev = 58817;
				return true;

			case ConfuserVersion.v14_r58852:
				minRev = 58852;
				maxRev = 67058;
				return true;

			case ConfuserVersion.v16_r69339:
				minRev = 69339;
				maxRev = 74637;
				return true;

			case ConfuserVersion.v17_r74708:
				minRev = 74708;
				maxRev = 75184;
				return true;

			case ConfuserVersion.v18_r75257:
				minRev = 75257;
				maxRev = 75720;
				return true;

			case ConfuserVersion.v19_r75725:
				minRev = 75725;
				maxRev = 76163;
				return true;

			case ConfuserVersion.v19_r76186:
				minRev = 76186;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
