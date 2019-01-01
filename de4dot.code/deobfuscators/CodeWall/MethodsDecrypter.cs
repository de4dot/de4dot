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

using System;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeWall {
	class MethodsDecrypter {
		static readonly byte[] newCodeHeader = new byte[6] { 0x2B, 4, 0, 0, 0, 0 };
		static readonly byte[] decryptKey = new byte[10] { 0x8D, 0xB5, 0x2C, 0x3A, 0x1F, 0xC7, 0x31, 0xC3, 0xCD, 0x47 };

		ModuleDefMD module;
		IMethod initMethod;

		public bool Detected => initMethod != null;
		public MethodsDecrypter(ModuleDefMD module) => this.module = module;

		public void Find() {
			foreach (var cctor in DeobUtils.GetInitCctors(module, 3)) {
				if (CheckCctor(cctor))
					return;
			}
		}

		bool CheckCctor(MethodDef method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as IMethod;
				if (calledMethod == null)
					continue;
				if (calledMethod.DeclaringType.Scope == module)
					return false;
				if (calledMethod.FullName != "System.Void Q::X()")
					return false;

				initMethod = calledMethod;
				return true;
			}
			return false;
		}

		public bool Decrypt(MyPEImage peImage, ref DumpedMethods dumpedMethods) {
			dumpedMethods = new DumpedMethods();

			bool decrypted = false;

			var methodDef = peImage.Metadata.TablesStream.MethodTable;
			for (uint rid = 1; rid <= methodDef.Rows; rid++) {
				var dm = new DumpedMethod();
				peImage.ReadMethodTableRowTo(dm, rid);

				if (dm.mdRVA == 0)
					continue;
				uint bodyOffset = peImage.RvaToOffset(dm.mdRVA);

				peImage.Reader.Position = bodyOffset;
				var mbHeader = MethodBodyParser.ParseMethodBody(ref peImage.Reader, out dm.code, out dm.extraSections);
				peImage.UpdateMethodHeaderInfo(dm, mbHeader);

				if (dm.code.Length < 6 || dm.code[0] != 0x2A || dm.code[1] != 0x2A)
					continue;

				int seed = BitConverter.ToInt32(dm.code, 2);
				Array.Copy(newCodeHeader, dm.code, newCodeHeader.Length);
				if (seed == 0)
					Decrypt(dm.code);
				else
					Decrypt(dm.code, seed);

				dumpedMethods.Add(dm);
				decrypted = true;
			}

			return decrypted;
		}

		void Decrypt(byte[] data) {
			for (int i = 6; i < data.Length; i++)
				data[i] ^= decryptKey[i % decryptKey.Length];
		}

		void Decrypt(byte[] data, int seed) {
			var key = new KeyGenerator(seed).Generate(data.Length);
			for (int i = 6; i < data.Length; i++)
				data[i] ^= key[i];
		}

		public void Deobfuscate(Blocks blocks) {
			if (initMethod == null)
				return;
			if (blocks.Method.Name != ".cctor")
				return;

			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Call)
						continue;
					var calledMethod = instr.Operand as IMethod;
					if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(calledMethod, initMethod))
						continue;
					block.Remove(i, 1);
					i--;
				}
			}
		}
	}
}
