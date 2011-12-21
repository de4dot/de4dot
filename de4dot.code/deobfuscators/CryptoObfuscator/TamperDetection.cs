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

using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class TamperDetection {
		ModuleDefinition module;
		TypeDefinition tamperType;
		MethodDefinition tamperMethod;

		public bool Detected {
			get { return tamperMethod != null; }
		}

		public TypeDefinition Type {
			get { return tamperType; }
		}

		public MethodDefinition Method {
			get { return tamperMethod; }
		}

		public TamperDetection(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			if (find(module.EntryPoint))
				return;
			if (find(DotNetUtils.getMethod(DotNetUtils.getModuleType(module), ".cctor")))
				return;
		}

		bool find(MethodDefinition methodToCheck) {
			if (methodToCheck == null)
				return false;

			foreach (var info in DotNetUtils.getCalledMethods(module, methodToCheck)) {
				var type = info.Item1;
				var method = info.Item2;

				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (type.Methods.Count < 3 || type.Methods.Count > 6)
					continue;
				if (DotNetUtils.getPInvokeMethod(type, "mscoree", "StrongNameSignatureVerificationEx") == null)
					continue;

				tamperType = type;
				tamperMethod = method;
				return true;
			}

			return false;
		}
	}
}
