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

using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeFort {
	class StringDecrypter {
		ModuleDefinition module;
		MethodDefinition decryptMethod;

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public MethodDefinition Method {
			get { return decryptMethod; }
		}

		public TypeDefinition Type {
			get { return decryptMethod == null ? null : decryptMethod.DeclaringType; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				var method = checkType(type);
				if (method == null)
					continue;

				decryptMethod = method;
			}
		}

		static MethodDefinition checkType(TypeDefinition type) {
			if (type.HasFields)
				return null;
			return checkMethods(type);
		}

		static MethodDefinition checkMethods(TypeDefinition type) {
			MethodDefinition decryptMethod = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					continue;
				if (!method.IsStatic || method.Body == null)
					return null;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.String)"))
					return null;
				if (!hasDouble(method, 3992.0))
					return null;

				decryptMethod = method;
			}
			return decryptMethod;
		}

		static bool hasDouble(MethodDefinition method, double value) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldc_R8)
					continue;
				if ((double)instr.Operand == value)
					return true;
			}
			return false;
		}

		public string decrypt(string s) {
			var bytes = new byte[s.Length];
			for (int i = 0; i < s.Length; i++)
				bytes[i] = (byte)(s[i] ^ 0x3F);
			return Encoding.UTF8.GetString(bytes);
		}
	}
}
