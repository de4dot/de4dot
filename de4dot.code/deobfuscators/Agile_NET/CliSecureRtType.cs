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

using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET {
	class CliSecureRtType {
		ModuleDefMD module;
		TypeDef cliSecureRtType;
		MethodDef postInitializeMethod;
		MethodDef initializeMethod;
		MethodDef stringDecrypterMethod;
		MethodDef loadMethod;
		bool foundSig;

		public bool Detected {
			get { return foundSig || cliSecureRtType != null; }
		}

		public TypeDef Type {
			get { return cliSecureRtType; }
		}

		public MethodDef StringDecrypterMethod {
			get { return stringDecrypterMethod; }
		}

		public MethodDef PostInitializeMethod {
			get { return postInitializeMethod; }
		}

		public MethodDef InitializeMethod {
			get { return initializeMethod; }
		}

		public MethodDef LoadMethod {
			get { return loadMethod; }
		}

		public CliSecureRtType(ModuleDefMD module) {
			this.module = module;
		}

		public CliSecureRtType(ModuleDefMD module, CliSecureRtType oldOne) {
			this.module = module;
			cliSecureRtType = lookup(oldOne.cliSecureRtType, "Could not find CliSecureRt type");
			postInitializeMethod = lookup(oldOne.postInitializeMethod, "Could not find postInitializeMethod method");
			initializeMethod = lookup(oldOne.initializeMethod, "Could not find initializeMethod method");
			stringDecrypterMethod = lookup(oldOne.stringDecrypterMethod, "Could not find stringDecrypterMethod method");
			loadMethod = lookup(oldOne.loadMethod, "Could not find loadMethod method");
			foundSig = oldOne.foundSig;
		}

		T lookup<T>(T def, string errorMessage) where T : class, ICodedToken {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find(byte[] moduleBytes) {
			if (cliSecureRtType != null)
				return;
			if (find2())
				return;
			if (find3())
				return;
			findNativeCode(moduleBytes);
		}

		static readonly string[] requiredFields1 = new string[] {
			"System.Boolean",
		};
		static readonly string[] requiredFields2 = new string[] {
			"System.Boolean",
			"System.Reflection.Assembly",
		};
		static readonly string[] requiredFields3 = new string[] {
			"System.Boolean",
			"System.Byte[]",
		};
		static readonly string[] requiredFields4 = new string[] {
			"System.Boolean",
			"System.Reflection.Assembly",
			"System.Byte[]",
		};
		bool find2() {
			foreach (var cctor in DeobUtils.getInitCctors(module, 3)) {
				foreach (var calledMethod in DotNetUtils.getCalledMethods(module, cctor)) {
					var type = calledMethod.DeclaringType;
					if (type.IsPublic)
						continue;
					var fieldTypes = new FieldTypes(type);
					if (!fieldTypes.exactly(requiredFields1) && !fieldTypes.exactly(requiredFields2) &&
						!fieldTypes.exactly(requiredFields3) && !fieldTypes.exactly(requiredFields4))
						continue;
					if (!hasInitializeMethod(type, "_Initialize") && !hasInitializeMethod(type, "_Initialize64"))
						continue;

					stringDecrypterMethod = findStringDecrypterMethod(type);
					initializeMethod = calledMethod;
					postInitializeMethod = findMethod(type, "System.Void", "PostInitialize", "()");
					loadMethod = findMethod(type, "System.IntPtr", "Load", "()");
					cliSecureRtType = type;
					return true;
				}
			}

			return false;
		}

		bool find3() {
			foreach (var type in module.Types) {
				if (type.Fields.Count != 1)
					continue;
				if (type.Fields[0].FieldSig.GetFieldType().GetFullName() != "System.Byte[]")
					continue;
				if (type.Methods.Count != 2)
					continue;
				if (type.FindStaticConstructor() == null)
					continue;
				var cs = type.FindMethod("cs");
				if (cs == null)
					continue;

				stringDecrypterMethod = cs;
				cliSecureRtType = type;
				return true;
			}

			return false;
		}

		static MethodDef findStringDecrypterMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (method.Body == null || !method.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.String)"))
					continue;

				return method;
			}

			return null;
		}

		static MethodDef findMethod(TypeDef type, string returnType, string name, string parameters) {
			var methodName = returnType + " " + type.FullName + "::" + name + parameters;
			foreach (var method in type.Methods) {
				if (method.Body == null || !method.IsStatic)
					continue;
				if (method.FullName != methodName)
					continue;

				return method;
			}

			return null;
		}

		static bool hasInitializeMethod(TypeDef type, string name) {
			var method = DotNetUtils.getPInvokeMethod(type, name);
			if (method == null)
				return false;
			var sig = method.MethodSig;
			if (sig.Params.Count != 1)
				return false;
			if (sig.Params[0].GetElementType() != ElementType.I)
				return false;
			var retType = sig.RetType.GetElementType();
			if (retType != ElementType.Void && retType != ElementType.I4)
				return false;
			return true;
		}

		bool findNativeCode(byte[] moduleBytes) {
			var bytes = moduleBytes != null ? moduleBytes : DeobUtils.readModule(module);
			using (var peImage = new MyPEImage(bytes))
				return foundSig = MethodsDecrypter.detect(peImage);
		}

		public bool isAtLeastVersion50() {
			return DotNetUtils.hasPinvokeMethod(cliSecureRtType, "LoadLibraryA");
		}

		public void findStringDecrypterMethod() {
			if (cliSecureRtType != null)
				return;

			foreach (var type in module.Types) {
				if (type.Fields.Count != 0)
					continue;
				if (type.Methods.Count != 1)
					continue;
				var cs = type.Methods[0];
				if (!isOldStringDecrypterMethod(cs))
					continue;

				cliSecureRtType = type;
				stringDecrypterMethod = cs;
				return;
			}
		}

		static bool isOldStringDecrypterMethod(MethodDef method) {
			if (method == null || method.Body == null || !method.IsStatic)
				return false;
			if (!DotNetUtils.isMethod(method, "System.String", "(System.String)"))
				return false;
			if (!DeobUtils.hasInteger(method, 0xFF))
				return false;

			return true;
		}
	}
}
