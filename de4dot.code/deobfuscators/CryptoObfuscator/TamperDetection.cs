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
	class TamperDetection {
		ModuleDefMD module;
		TypeDef tamperType;
		MethodDef tamperMethod;
		FrameworkType frameworkType;

		public bool Detected => tamperMethod != null;
		public TypeDef Type => tamperType;
		public MethodDef Method => tamperMethod;

		public TamperDetection(ModuleDefMD module) {
			this.module = module;
			frameworkType = DotNetUtils.GetFrameworkType(module);
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
				bool result = false;
				switch (frameworkType) {
				case FrameworkType.Desktop:
					result = FindDesktop(method);
					break;
				case FrameworkType.Silverlight:
					result = FindSilverlight(method);
					break;
				case FrameworkType.CompactFramework:
					result = FindCompactFramework(method);
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

		bool FindDesktop(MethodDef method) {
			var type = method.DeclaringType;

			if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
				return false;
			if (type.Methods.Count < 3 || type.Methods.Count > 31)
				return false;
			if (DotNetUtils.GetPInvokeMethod(type, "mscoree", "StrongNameSignatureVerificationEx") != null) {
			}
			else if (DotNetUtils.GetPInvokeMethod(type, "mscoree", "CLRCreateInstance") != null) {
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
		bool FindSilverlight(MethodDef method) {
			if (!new LocalTypes(method).Exactly(requiredLocals_sl))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Int32 System.String::get_Length()"))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)"))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()"))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.String System.Reflection.Assembly::get_FullName()"))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()"))
				return false;
			if (DotNetUtils.CallsMethod(method, "System.String", "(System.Reflection.Assembly)")) {
			}
			else if (DotNetUtils.CallsMethod(method, "System.String System.Reflection.AssemblyName::get_Name()")) {
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
		bool FindCompactFramework(MethodDef method) {
			if (!new LocalTypes(method).Exactly(requiredLocals_cf))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Int32 System.String::get_Length()"))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)"))
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()"))
				return false;

			if (DotNetUtils.CallsMethod(method, "System.Byte[]", "(System.Reflection.Assembly)") &&
				DotNetUtils.CallsMethod(method, "System.String", "(System.Reflection.Assembly)")) {
			}
			else if (DotNetUtils.CallsMethod(method, "System.Reflection.AssemblyName System.Reflection.Assembly::GetName()") &&
					DotNetUtils.CallsMethod(method, "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()")) {
			}
			else
				return false;

			return true;
		}
	}
}
