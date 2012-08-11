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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class AntiDumping : IVersionProvider {
		ModuleDefinition module;
		MethodDefinition initMethod;
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

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public TypeDefinition Type {
			get { return initMethod != null ? initMethod.DeclaringType : null; }
		}

		public bool Detected {
			get { return initMethod != null; }
		}

		public AntiDumping(ModuleDefinition module) {
			this.module = module;
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			if (checkMethod(simpleDeobfuscator, DotNetUtils.getModuleTypeCctor(module)))
				return;
		}

		bool checkMethod(ISimpleDeobfuscator simpleDeobfuscator, MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (calledMethod == null || !calledMethod.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Void", "()"))
					continue;
				var type = calledMethod.DeclaringType;
				if (type.NestedTypes.Count > 0)
					continue;

				simpleDeobfuscator.deobfuscate(calledMethod, true);
				if (checkType(type, calledMethod)) {
					initMethod = calledMethod;
					return true;
				}
			}
			return false;
		}

		bool checkType(TypeDefinition type, MethodDefinition initMethod) {
			return checkType_v14_r58564(type, initMethod) ||
				checkType_v14_r58852(type, initMethod);
		}

		bool checkType_v14_r58564(TypeDefinition type, MethodDefinition initMethod) {
			var virtualProtect = DotNetUtils.getPInvokeMethod(type, "VirtualProtect");
			if (virtualProtect == null)
				return false;
			if (!DotNetUtils.callsMethod(initMethod, "System.IntPtr System.Runtime.InteropServices.Marshal::GetHINSTANCE(System.Reflection.Module)"))
				return false;
			if (ConfuserUtils.countCalls(initMethod, virtualProtect) != 3)
				return false;
			if (!DeobUtils.hasInteger(initMethod, 224))
				return false;
			if (!DeobUtils.hasInteger(initMethod, 240))
				return false;
			if (!DeobUtils.hasInteger(initMethod, 267))
				return false;

			version = ConfuserVersion.v14_r58564;
			return true;
		}

		bool checkType_v14_r58852(TypeDefinition type, MethodDefinition initMethod) {
			var virtualProtect = DotNetUtils.getPInvokeMethod(type, "VirtualProtect");
			if (virtualProtect == null)
				return false;
			if (!DotNetUtils.callsMethod(initMethod, "System.IntPtr System.Runtime.InteropServices.Marshal::GetHINSTANCE(System.Reflection.Module)"))
				return false;
			int virtualProtectCalls = ConfuserUtils.countCalls(initMethod, virtualProtect);
			if (virtualProtectCalls != 14 && virtualProtectCalls != 16)
				return false;
			if (!DeobUtils.hasInteger(initMethod, 0x3C))
				return false;
			if (!DeobUtils.hasInteger(initMethod, 0x6c64746e))
				return false;
			if (!DeobUtils.hasInteger(initMethod, 0x6c642e6c))
				return false;
			if (!DeobUtils.hasInteger(initMethod, 0x6f43744e))
				return false;
			if (!DeobUtils.hasInteger(initMethod, 0x6e69746e))
				return false;
			int locallocs = ConfuserUtils.countOpCode(initMethod, Code.Localloc);

			if (DeobUtils.hasInteger(initMethod, 0x18))
				version = ConfuserVersion.v14_r58852;
			else if (virtualProtectCalls == 16)
				version = ConfuserVersion.v16_r69339;
			else if (virtualProtectCalls == 14) {
				if (locallocs == 2)
					version = ConfuserVersion.v17_r74708;
				else if (locallocs == 1) {
					if (DotNetUtils.hasString(initMethod, "<Unknown>"))
						version = ConfuserVersion.v18_r75257;
					else if (isRev75725(initMethod))
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

		static bool isRev75725(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 9; i++) {
				if (!DotNetUtils.isLdcI4(instrs[i]) || DotNetUtils.getLdcI4Value(instrs[i]) != 8)
					continue;
				if (!DotNetUtils.isLdcI4(instrs[i + 1]) || DotNetUtils.getLdcI4Value(instrs[i + 1]) != 64)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Ldloca && instrs[i + 2].OpCode.Code != Code.Ldloca_S)
					continue;
				var call = instrs[i + 3];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null || calledMethod.PInvokeInfo == null || calledMethod.PInvokeInfo.EntryPoint != "VirtualProtect")
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Pop)
					continue;

				var ldloc = instrs[i + 5];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				var local = DotNetUtils.getLocalVar(method.Body.Variables, ldloc);
				if (local == null)
					continue;

				if (!DotNetUtils.isLdcI4(instrs[i + 6]) || DotNetUtils.getLdcI4Value(instrs[i + 6]) != 0)
					continue;
				if (instrs[i + 7].OpCode.Code != Code.Stind_I4)
					continue;

				ldloc = instrs[i + 8];
				if (!DotNetUtils.isLdloc(ldloc) || local != DotNetUtils.getLocalVar(method.Body.Variables, ldloc))
					continue;
				if (!DotNetUtils.isLdcI4(instrs[i + 9]) || DotNetUtils.getLdcI4Value(instrs[i + 9]) != 4)
					continue;

				return true;
			}
			return false;
		}

		public bool getRevisionRange(out int minRev, out int maxRev) {
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
