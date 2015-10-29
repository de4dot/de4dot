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

using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class AntiDebugger {
		ModuleDefMD module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IDeobfuscator deob;
		TypeDef antiDebuggerType;
		MethodDef antiDebuggerMethod;

		public TypeDef Type {
			get { return antiDebuggerType; }
		}

		public MethodDef Method {
			get { return antiDebuggerMethod; }
		}

		public AntiDebugger(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public void Find() {
			if (Find(module.EntryPoint))
				return;
			if (Find(DotNetUtils.GetModuleTypeCctor(module)))
				return;
		}

		bool Find(MethodDef methodToCheck) {
			if (methodToCheck == null)
				return false;
			foreach (var method in DotNetUtils.GetCalledMethods(module, methodToCheck)) {
				var type = method.DeclaringType;

				if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;
				if (DotNetUtils.GetPInvokeMethod(type, "kernel32", "LoadLibrary") == null)
					continue;
				if (DotNetUtils.GetPInvokeMethod(type, "kernel32", "GetProcAddress") == null)
					continue;
				Deobfuscate(method);
				if (!ContainsString(method, "debugger is activ") &&
					!ContainsString(method, "debugger is running") &&
					!ContainsString(method, "Debugger detected") &&
					!ContainsString(method, "Debugger was detected") &&
					!ContainsString(method, "{0} was detected") &&
					!ContainsString(method, "run under") &&
					!ContainsString(method, "run with") &&
					!ContainsString(method, "started under") &&
					!ContainsString(method, "{0} detected") &&
					!ContainsString(method, "{0} found"))
					continue;

				antiDebuggerType = type;
				antiDebuggerMethod = method;
				return true;
			}

			return false;
		}

		void Deobfuscate(MethodDef method) {
			simpleDeobfuscator.Deobfuscate(method);
			simpleDeobfuscator.DecryptStrings(method, deob);
		}

		bool ContainsString(MethodDef method, string part) {
			foreach (var s in DotNetUtils.GetCodeStrings(method)) {
				if (s.Contains(part))
					return true;
			}
			return false;
		}
	}
}
