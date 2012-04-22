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
using System.IO;
using Mono.Cecil;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.CliSecure {
	class CliSecureRtType {
		ModuleDefinition module;
		TypeDefinition cliSecureRtType;
		MethodDefinition postInitializeMethod;
		MethodDefinition initializeMethod;
		MethodDefinition stringDecrypterMethod;
		MethodDefinition loadMethod;
		bool foundSig;

		public bool Detected {
			get { return foundSig || cliSecureRtType != null; }
		}

		public TypeDefinition Type {
			get { return cliSecureRtType; }
		}

		public MethodDefinition StringDecrypterMethod {
			get { return stringDecrypterMethod; }
		}

		public MethodDefinition PostInitializeMethod {
			get { return postInitializeMethod; }
		}

		public MethodDefinition InitializeMethod {
			get { return initializeMethod; }
		}

		public MethodDefinition LoadMethod {
			get { return loadMethod; }
		}

		public CliSecureRtType(ModuleDefinition module) {
			this.module = module;
		}

		public CliSecureRtType(ModuleDefinition module, CliSecureRtType oldOne) {
			this.module = module;
			cliSecureRtType = lookup(oldOne.cliSecureRtType, "Could not find CliSecureRt type");
			postInitializeMethod = lookup(oldOne.postInitializeMethod, "Could not find postInitializeMethod method");
			initializeMethod = lookup(oldOne.initializeMethod, "Could not find initializeMethod method");
			stringDecrypterMethod = lookup(oldOne.stringDecrypterMethod, "Could not find stringDecrypterMethod method");
			loadMethod = lookup(oldOne.loadMethod, "Could not find loadMethod method");
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			if (cliSecureRtType != null)
				return;
			if (find2())
				return;
			if (findOld())
				return;
			findNativeCode();
		}

		bool find2() {
			foreach (var type in module.Types) {
				if (type.Namespace != "")
					continue;
				var typeName = type.FullName;

				MethodDefinition cs = null;
				MethodDefinition initialize = null;
				MethodDefinition postInitialize = null;
				MethodDefinition load = null;

				int methods = 0;
				foreach (var method in type.Methods) {
					if (method.FullName == "System.String " + typeName + "::cs(System.String)") {
						cs = method;
						methods++;
					}
					else if (method.FullName == "System.Void " + typeName + "::Initialize()") {
						initialize = method;
						methods++;
					}
					else if (method.FullName == "System.Void " + typeName + "::PostInitialize()") {
						postInitialize = method;
						methods++;
					}
					else if (method.FullName == "System.IntPtr " + typeName + "::Load()") {
						load = method;
						methods++;
					}
				}
				if (methods == 0 || (methods == 1 && initialize != null))
					continue;

				stringDecrypterMethod = cs;
				initializeMethod = initialize;
				postInitializeMethod = postInitialize;
				loadMethod = load;
				cliSecureRtType = type;
				return true;
			}

			return false;
		}

		bool findOld() {
			var methodToCheck = DotNetUtils.getModuleTypeCctor(module);
			if (methodToCheck == null)
				return false;

			foreach (var calledMethod in DotNetUtils.getCalledMethods(module, methodToCheck)) {
				var type = calledMethod.DeclaringType;
				if (!hasPinvokeMethod(type, "_Initialize"))
					continue;
				if (!hasPinvokeMethod(type, "_Initialize64"))
					continue;

				initializeMethod = calledMethod;
				cliSecureRtType = type;
				return true;
			}

			return false;
		}

		bool findNativeCode() {
			if ((module.Attributes & ModuleAttributes.ILOnly) != 0)
				return false;

			var peImage = new PeImage(new FileStream(module.FullyQualifiedName, FileMode.Open, FileAccess.Read, FileShare.Read));
			foundSig = MethodsDecrypter.detect(peImage);
			return foundSig;
		}

		static bool hasPinvokeMethod(TypeDefinition type, string methodName) {
			foreach (var method in type.Methods) {
				if (method.PInvokeInfo == null)
					continue;
				if (method.PInvokeInfo.EntryPoint == methodName)
					return true;
			}
			return false;
		}
	}
}
