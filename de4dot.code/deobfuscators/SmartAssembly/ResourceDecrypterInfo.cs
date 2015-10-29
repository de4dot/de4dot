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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class ResourceDecrypterInfo {
		ModuleDefMD module;
		MethodDef simpleZipTypeDecryptMethod;

		public byte[] DES_Key { get; private set; }
		public byte[] DES_IV  { get; private set; }
		public byte[] AES_Key { get; private set; }
		public byte[] AES_IV  { get; private set; }

		public bool CanDecrypt {
			get { return simpleZipTypeDecryptMethod != null; }
		}

		public ResourceDecrypterInfo(ModuleDefMD module) {
			this.module = module;
		}

		public ResourceDecrypterInfo(ModuleDefMD module, MethodDef simpleZipTypeDecryptMethod, ISimpleDeobfuscator simpleDeobfuscator)
			: this(module) {
			SetSimpleZipType(simpleZipTypeDecryptMethod, simpleDeobfuscator);
		}

		public void SetSimpleZipType(MethodDef method, ISimpleDeobfuscator simpleDeobfuscator) {
			if (simpleZipTypeDecryptMethod != null || method == null)
				return;
			simpleZipTypeDecryptMethod = method;
			Initialize(simpleDeobfuscator, method);
		}

		void Initialize(ISimpleDeobfuscator simpleDeobfuscator, MethodDef method) {
			var desList = new List<byte[]>(2);
			var aesList = new List<byte[]>(2);

			var instructions = method.Body.Instructions;
			simpleDeobfuscator.Deobfuscate(method);
			for (int i = 0; i <= instructions.Count - 2; i++) {
				var ldtoken = instructions[i];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = DotNetUtils.GetField(module, ldtoken.Operand as IField);
				if (field == null)
					continue;
				if (field.InitialValue == null)
					continue;

				var call = instructions[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as IMethod;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "(System.Array,System.RuntimeFieldHandle)"))
					continue;

				if (field.InitialValue.Length == 8)
					desList.Add(field.InitialValue);
				else if (field.InitialValue.Length == 16)
					aesList.Add(field.InitialValue);
			}

			if (desList.Count >= 2) {
				DES_Key = desList[desList.Count - 2];
				DES_IV  = desList[desList.Count - 1];
			}
			if (aesList.Count >= 2) {
				AES_Key = aesList[aesList.Count - 2];
				AES_IV  = aesList[aesList.Count - 1];
			}
		}
	}
}
