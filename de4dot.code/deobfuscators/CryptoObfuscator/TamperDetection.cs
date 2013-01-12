/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
	class TamperDetection {
		ModuleDefMD module;
		TypeDef tamperType;
		MethodDef tamperMethod;
		FrameworkType frameworkType;

		public bool Detected {
			get { return tamperMethod != null; }
		}

		public TypeDef Type {
			get { return tamperType; }
		}

		public MethodDef Method {
			get { return tamperMethod; }
		}

		public TamperDetection(ModuleDefMD module) {
			this.module = module;
			frameworkType = DotNetUtils.getFrameworkType(module);
		}

		public void find() {
			if (find(module.EntryPoint))
				return;
			if (find(DotNetUtils.getModuleTypeCctor(module)))
				return;
		}

		bool find(MethodDef methodToCheck) {
			if (methodToCheck == null)
				return false;

			foreach (var method in DotNetUtils.getCalledMethods(module, methodToCheck)) {
				bool result = false;
				switch (frameworkType) {
				case FrameworkType.Desktop:
					result = findDesktop(method);
					break;
				case FrameworkType.Silverlight:
					result = findSilverlight(method);
					break;
				case FrameworkType.CompactFramework:
					result = findCompactFramework(method);
					break;
				}
				if (!result)
					continue;

				tamperType = method.DeclaringType;
				tamperMethod = method;
				return true;
			}

			return false;
		}

		bool findDesktop(MethodDef method) {
			var type = method.DeclaringType;

			if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
				return false;
			if (type.Methods.Count < 3 || type.Methods.Count > 20)
				return false;
			if (DotNetUtils.getPInvokeMethod(type, "mscoree", "StrongNameSignatureVerificationEx") != null) {
			}
			else if (DotNetUtils.getPInvokeMethod(type, "mscoree", "CLRCreateInstance") != null) {
				if (type.NestedTypes.Count != 3)
					return false;
				if (!type.NestedTypes[0].IsInterface || !type.NestedTypes[1].IsInterface || !type.NestedTypes[2].IsInterface)
					return false;
			}
			else
				return false;

			return true;
		}

		static string[] requiredLocals_sl = new string[] {
			"System.Boolean",
			"System.Byte[]",
			"System.Int32",
			"System.Reflection.AssemblyName",
			"System.String",
		};
		bool findSilverlight(MethodDef method) {
			if (!new LocalTypes(method).exactly(requiredLocals_sl))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Int32 System.String::get_Length()"))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)"))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()"))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.String System.Reflection.Assembly::get_FullName()"))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()"))
				return false;
			if (DotNetUtils.callsMethod(method, "System.String", "(System.Reflection.Assembly)")) {
			}
			else if (DotNetUtils.callsMethod(method, "System.String System.Reflection.AssemblyName::get_Name()")) {
			}
			else
				return false;

			return true;
		}

		static string[] requiredLocals_cf = new string[] {
			"System.Boolean",
			"System.Byte[]",
			"System.Int32",
			"System.String",
		};
		bool findCompactFramework(MethodDef method) {
			if (!new LocalTypes(method).exactly(requiredLocals_cf))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Int32 System.String::get_Length()"))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)"))
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()"))
				return false;

			if (DotNetUtils.callsMethod(method, "System.Byte[]", "(System.Reflection.Assembly)") &&
				DotNetUtils.callsMethod(method, "System.String", "(System.Reflection.Assembly)")) {
			}
			else if (DotNetUtils.callsMethod(method, "System.Reflection.AssemblyName System.Reflection.Assembly::GetName()") &&
					DotNetUtils.callsMethod(method, "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()")) {
			}
			else
				return false;

			return true;
		}
	}
}
