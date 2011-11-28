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

using System;
using Mono.Cecil;

namespace de4dot.deobfuscators.CliSecure {
	class CliSecureRtType {
		ModuleDefinition module;
		TypeDefinition cliSecureRtType;
		MethodDefinition postInitializeMethod;
		MethodDefinition initializeMethod;
		MethodDefinition stringDecrypterMethod;
		MethodDefinition loadMethod;

		public bool Detected {
			get { return cliSecureRtType != null; }
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
				if (methods == 0)
					continue;

				stringDecrypterMethod = cs;
				initializeMethod = initialize;
				postInitializeMethod = postInitialize;
				loadMethod = load;
				cliSecureRtType = type;
				return;
			}
		}
	}
}
