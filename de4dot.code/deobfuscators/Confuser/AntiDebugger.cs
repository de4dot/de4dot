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
	class AntiDebugger : IVersionProvider {
		ModuleDefMD module;
		MethodDef initMethod;
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v14_r57588_normal,
			v14_r57588_safe,
			v14_r60785_normal,
			v16_r61954_normal,
			v16_r61954_safe,
			v17_r73822_normal,
			v17_r73822_safe,
			v17_r74021_normal,
			v17_r74021_safe,
			v19_r76119_safe,
			v19_r78363_normal,
			v19_r78363_safe,
		}

		public MethodDef InitMethod => initMethod;
		public TypeDef Type => initMethod?.DeclaringType;
		public bool Detected => initMethod != null;
		public AntiDebugger(ModuleDefMD module) => this.module = module;

		public void Find() {
			if (CheckMethod(DotNetUtils.GetModuleTypeCctor(module)))
				return;
		}

		bool CheckMethod(MethodDef method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "()"))
					continue;
				var type = calledMethod.DeclaringType;
				if (type == null)
					continue;

				if (CheckMethod_normal(type, calledMethod) || CheckMethod_safe(type, calledMethod)) {
					initMethod = calledMethod;
					return true;
				}
			}

			return false;
		}

		static bool CheckProfilerStrings1(MethodDef method) {
			if (!DotNetUtils.HasString(method, "COR_ENABLE_PROFILING"))
				return false;
			if (!DotNetUtils.HasString(method, "COR_PROFILER"))
				return false;

			return true;
		}

		static bool CheckProfilerStrings2(MethodDef method) {
			if (!DotNetUtils.HasString(method, "COR_"))
				return false;
			if (!DotNetUtils.HasString(method, "ENABLE_PROFILING"))
				return false;
			if (!DotNetUtils.HasString(method, "PROFILER"))
				return false;

			return true;
		}

		static MethodDef GetAntiDebugMethod(TypeDef type, MethodDef initMethod) {
			foreach (var method in type.Methods) {
				if (method.Body == null || method == initMethod)
					continue;
				if (!method.IsStatic || method.Name == ".cctor")
					continue;
				if (!method.IsPrivate)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "()") && !DotNetUtils.IsMethod(method, "System.Void", "(System.Object)"))
					continue;

				return method;
			}
			return null;
		}

		bool CheckMethod_normal(TypeDef type, MethodDef initMethod) {
			var ntQueryInformationProcess = DotNetUtils.GetPInvokeMethod(type, "ntdll", "NtQueryInformationProcess");
			if (ntQueryInformationProcess == null)
				return false;
			if (DotNetUtils.GetPInvokeMethod(type, "ntdll", "NtSetInformationProcess") == null)
				return false;
			if (DotNetUtils.GetPInvokeMethod(type, "kernel32", "CloseHandle") == null)
				return false;
			var antiDebugMethod = GetAntiDebugMethod(type, initMethod);
			if (antiDebugMethod == null)
				return false;
			bool hasDebuggerStrings = DotNetUtils.HasString(antiDebugMethod, "Debugger detected (Managed)");

			if (DotNetUtils.CallsMethod(initMethod, "System.Void System.Threading.Thread::.ctor(System.Threading.ParameterizedThreadStart)")) {
				int failFastCalls = ConfuserUtils.CountCalls(antiDebugMethod, "System.Void System.Environment::FailFast(System.String)");
				if (failFastCalls != 6 && failFastCalls != 8)
					return false;

				if (!CheckProfilerStrings1(initMethod))
					return false;

				if (!DotNetUtils.CallsMethod(antiDebugMethod, "System.Void System.Threading.Thread::.ctor(System.Threading.ParameterizedThreadStart)")) {
					if (!hasDebuggerStrings)
						return false;
					if (ConfuserUtils.CountCalls(antiDebugMethod, ntQueryInformationProcess) != 2)
						return false;
					version = ConfuserVersion.v16_r61954_normal;
				}
				else if (failFastCalls == 8) {
					if (!hasDebuggerStrings)
						return false;
					if (ConfuserUtils.CountCalls(antiDebugMethod, ntQueryInformationProcess) != 2)
						return false;
					version = ConfuserVersion.v17_r73822_normal;
				}
				else if (failFastCalls == 6) {
					if (DotNetUtils.GetPInvokeMethod(type, "IsDebuggerPresent") == null)
						return false;
					if (ConfuserUtils.CountCalls(antiDebugMethod, ntQueryInformationProcess) != 0)
						return false;
					if (hasDebuggerStrings)
						version = ConfuserVersion.v17_r74021_normal;
					else
						version = ConfuserVersion.v19_r78363_normal;
				}
				else
					return false;
			}
			else if (!DotNetUtils.CallsMethod(initMethod, "System.Void System.Threading.ThreadStart::.ctor(System.Object,System.IntPtr)")) {
				if (!hasDebuggerStrings)
					return false;
				if (!DotNetUtils.CallsMethod(initMethod, "System.Void System.Diagnostics.Process::EnterDebugMode()"))
					return false;
				if (!CheckProfilerStrings1(antiDebugMethod))
					return false;
				version = ConfuserVersion.v14_r57588_normal;
			}
			else {
				if (!hasDebuggerStrings)
					return false;
				if (!DotNetUtils.CallsMethod(initMethod, "System.Void System.Diagnostics.Process::EnterDebugMode()"))
					return false;
				if (!CheckProfilerStrings1(antiDebugMethod))
					return false;
				version = ConfuserVersion.v14_r60785_normal;
			}

			return true;
		}

		bool CheckMethod_safe(TypeDef type, MethodDef initMethod) {
			if (type == DotNetUtils.GetModuleType(module)) {
				if (!DotNetUtils.HasString(initMethod, "Debugger detected (Managed)"))
					return false;
				if (!CheckProfilerStrings1(initMethod))
					return false;

				version = ConfuserVersion.v14_r57588_safe;
			}
			else {
				var ntQueryInformationProcess = DotNetUtils.GetPInvokeMethod(type, "ntdll", "NtQueryInformationProcess");
				if (ntQueryInformationProcess == null)
					return false;
				if (DotNetUtils.GetPInvokeMethod(type, "ntdll", "NtSetInformationProcess") == null)
					return false;
				if (DotNetUtils.GetPInvokeMethod(type, "kernel32", "CloseHandle") == null)
					return false;
				var antiDebugMethod = GetAntiDebugMethod(type, initMethod);
				if (antiDebugMethod == null)
					return false;
				bool hasDebuggerStrings = DotNetUtils.HasString(antiDebugMethod, "Debugger detected (Managed)") ||
						DotNetUtils.HasString(antiDebugMethod, "Debugger is detected (Managed)");
				if (!DotNetUtils.CallsMethod(initMethod, "System.Void System.Threading.Thread::.ctor(System.Threading.ParameterizedThreadStart)"))
					return false;
				if (ConfuserUtils.CountCalls(antiDebugMethod, ntQueryInformationProcess) != 0)
					return false;
				if (!CheckProfilerStrings1(initMethod) && !CheckProfilerStrings2(initMethod))
					return false;

				int failFastCalls = ConfuserUtils.CountCalls(antiDebugMethod, "System.Void System.Environment::FailFast(System.String)");
				if (failFastCalls != 2)
					return false;

				if (hasDebuggerStrings) {
					if (!DotNetUtils.CallsMethod(antiDebugMethod, "System.Void System.Threading.Thread::.ctor(System.Threading.ParameterizedThreadStart)"))
						version = ConfuserVersion.v16_r61954_safe;
					else if (DotNetUtils.GetPInvokeMethod(type, "IsDebuggerPresent") == null)
						version = ConfuserVersion.v17_r73822_safe;
					else if (CheckProfilerStrings1(initMethod))
						version = ConfuserVersion.v17_r74021_safe;
					else
						version = ConfuserVersion.v19_r76119_safe;
				}
				else {
					version = ConfuserVersion.v19_r78363_safe;
				}
			}

			return true;
		}

		public bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v14_r57588_safe:
				minRev = 57588;
				maxRev = 60787;
				return true;

			case ConfuserVersion.v16_r61954_safe:
				minRev = 61954;
				maxRev = 73791;
				return true;

			case ConfuserVersion.v17_r73822_safe:
				minRev = 73822;
				maxRev = 73822;
				return true;

			case ConfuserVersion.v17_r74021_safe:
				minRev = 74021;
				maxRev = 76101;
				return true;

			case ConfuserVersion.v19_r76119_safe:
				minRev = 76119;
				maxRev = 78342;
				return true;

			case ConfuserVersion.v19_r78363_safe:
				minRev = 78363;
				maxRev = int.MaxValue;
				return true;

			case ConfuserVersion.v14_r57588_normal:
				minRev = 57588;
				maxRev = 60408;
				return true;

			case ConfuserVersion.v14_r60785_normal:
				minRev = 60785;
				maxRev = 60787;
				return true;

			case ConfuserVersion.v16_r61954_normal:
				minRev = 61954;
				maxRev = 73791;
				return true;

			case ConfuserVersion.v17_r73822_normal:
				minRev = 73822;
				maxRev = 73822;
				return true;

			case ConfuserVersion.v17_r74021_normal:
				minRev = 74021;
				maxRev = 78342;
				return true;

			case ConfuserVersion.v19_r78363_normal:
				minRev = 78363;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
