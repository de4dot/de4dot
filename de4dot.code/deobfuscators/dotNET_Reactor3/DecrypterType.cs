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
using System.Collections.Generic;
using System.Text;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor3 {
	// Find the type that decrypts strings and calls the native lib
	class DecrypterType {
		ModuleDefinition module;
		TypeDefinition decrypterType;
		MethodDefinition stringDecrypter1;
		MethodDefinition stringDecrypter2;
		List<MethodDefinition> initMethods = new List<MethodDefinition>();

		public bool Detected {
			get { return decrypterType != null; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public MethodDefinition StringDecrypter1 {
			get { return stringDecrypter1; }
		}

		public MethodDefinition StringDecrypter2 {
			get { return stringDecrypter2; }
		}

		public IEnumerable<MethodDefinition> InitMethods {
			get { return initMethods; }
		}

		public IEnumerable<MethodDefinition> StringDecrypters {
			get {
				return new List<MethodDefinition> {
					stringDecrypter1,
					stringDecrypter2,
				};
			}
		}

		public DecrypterType(ModuleDefinition module) {
			this.module = module;
		}

		public DecrypterType(ModuleDefinition module, DecrypterType oldOne) {
			this.module = module;
			this.decrypterType = lookup(oldOne.decrypterType, "Could not find decrypterType");
			this.stringDecrypter1 = lookup(oldOne.stringDecrypter1, "Could not find stringDecrypter1");
			this.stringDecrypter2 = lookup(oldOne.stringDecrypter2, "Could not find stringDecrypter2");
			foreach (var method in oldOne.initMethods)
				initMethods.Add(lookup(method, "Could not find initMethod"));
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			foreach (var type in module.Types) {
				if (type.FullName != "<PrivateImplementationDetails>{B4838DC1-AC79-43d1-949F-41B518B904A8}")
					continue;

				decrypterType = type;
				stringDecrypter1 = addStringDecrypter(type, "CS$0$0004");
				stringDecrypter2 = addStringDecrypter(type, "CS$0$0005");
				foreach (var method in type.Methods) {
					if (DotNetUtils.isMethod(method, "System.Void", "()"))
						initMethods.Add(method);
				}
				return;
			}
		}

		MethodDefinition addStringDecrypter(TypeDefinition type, string name) {
			var method = DotNetUtils.getMethod(type, name);
			if (method == null)
				return null;
			if (!DotNetUtils.isMethod(method, "System.String", "(System.String)"))
				return null;
			return method;
		}

		public string decrypt1(string s) {
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
				sb.Append((char)(0xFF - (byte)c));
			return sb.ToString();
		}

		public string decrypt2(string s) {
			return Encoding.Unicode.GetString(Convert.FromBase64String(s));
		}
	}
}
