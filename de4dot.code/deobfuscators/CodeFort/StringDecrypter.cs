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

using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeFort {
	class StringDecrypter {
		ModuleDefMD module;
		MethodDef decryptMethod;

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public MethodDef Method {
			get { return decryptMethod; }
		}

		public TypeDef Type {
			get { return decryptMethod == null ? null : decryptMethod.DeclaringType; }
		}

		public StringDecrypter(ModuleDefMD module) {
			this.module = module;
		}

		public void Find() {
			foreach (var type in module.Types) {
				var method = CheckType(type);
				if (method == null)
					continue;

				decryptMethod = method;
			}
		}

		static MethodDef CheckType(TypeDef type) {
			if (type.HasFields)
				return null;
			return CheckMethods(type);
		}

		static MethodDef CheckMethods(TypeDef type) {
			MethodDef decryptMethod = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					continue;
				if (!method.IsStatic || method.Body == null)
					return null;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.String)"))
					return null;
				if (!HasDouble(method, 3992.0))
					return null;

				decryptMethod = method;
			}
			return decryptMethod;
		}

		static bool HasDouble(MethodDef method, double value) {
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

		public string Decrypt(string s) {
			var bytes = new byte[s.Length];
			for (int i = 0; i < s.Length; i++)
				bytes[i] = (byte)(s[i] ^ 0x3F);
			return Encoding.UTF8.GetString(bytes);
		}
	}
}
