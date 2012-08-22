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

using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class AntiDebugger {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IDeobfuscator deob;
		TypeDefinition antiDebuggerType;
		MethodDefinition antiDebuggerMethod;

		public TypeDefinition Type {
			get { return antiDebuggerType; }
		}

		public MethodDefinition Method {
			get { return antiDebuggerMethod; }
		}

		public AntiDebugger(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public void find() {
			if (find(module.EntryPoint))
				return;
			if (find(DotNetUtils.getModuleTypeCctor(module)))
				return;
		}

		bool find(MethodDefinition methodToCheck) {
			if (methodToCheck == null)
				return false;
			foreach (var method in DotNetUtils.getCalledMethods(module, methodToCheck)) {
				var type = method.DeclaringType;

				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (DotNetUtils.getPInvokeMethod(type, "kernel32", "LoadLibrary") == null)
					continue;
				if (DotNetUtils.getPInvokeMethod(type, "kernel32", "GetProcAddress") == null)
					continue;
				deobfuscate(method);
				if (!containsString(method, "debugger is activ") &&
					!containsString(method, "debugger is running") &&
					!containsString(method, "run under a debugger") &&
					!containsString(method, "run under debugger") &&
					!containsString(method, "Debugger detected") &&
					!containsString(method, "Debugger was detected"))
					continue;

				antiDebuggerType = type;
				antiDebuggerMethod = method;
				return true;
			}

			return false;
		}

		void deobfuscate(MethodDefinition method) {
			simpleDeobfuscator.deobfuscate(method);
			simpleDeobfuscator.decryptStrings(method, deob);
		}

		bool containsString(MethodDefinition method, string part) {
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				if (s.Contains(part))
					return true;
			}
			return false;
		}
	}
}
