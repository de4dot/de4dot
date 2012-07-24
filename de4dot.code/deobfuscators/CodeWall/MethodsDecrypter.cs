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
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.MyStuff;
using de4dot.PE;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeWall {
	class MethodsDecrypter {
		static readonly byte[] newCodeHeader = new byte[6] { 0x2B, 4, 0, 0, 0, 0 };
		static readonly byte[] decryptKey = new byte[10] { 0x8D, 0xB5, 0x2C, 0x3A, 0x1F, 0xC7, 0x31, 0xC3, 0xCD, 0x47 };

		ModuleDefinition module;
		MethodReference initMethod;

		public bool Detected {
			get { return initMethod != null; }
		}

		public AssemblyNameReference AssemblyNameReference {
			get { return initMethod == null ? null : (AssemblyNameReference)initMethod.DeclaringType.Scope; }
		}

		public MethodsDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var cctor in DeobUtils.getInitCctors(module, 3)) {
				if (checkCctor(cctor))
					return;
			}
		}

		bool checkCctor(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodReference;
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

		public bool decrypt(PeImage peImage, ref DumpedMethods dumpedMethods) {
			dumpedMethods = new DumpedMethods();

			bool decrypted = false;

			var metadataTables = peImage.Cor20Header.createMetadataTables();
			var methodDef = metadataTables.getMetadataType(MetadataIndex.iMethodDef);
			uint methodDefOffset = methodDef.fileOffset;
			for (int i = 0; i < methodDef.rows; i++, methodDefOffset += methodDef.totalSize) {
				uint bodyRva = peImage.offsetReadUInt32(methodDefOffset);
				if (bodyRva == 0)
					continue;
				uint bodyOffset = peImage.rvaToOffset(bodyRva);

				var dm = new DumpedMethod();
				dm.token = (uint)(0x06000001 + i);

				byte[] code, extraSections;
				peImage.Reader.BaseStream.Position = bodyOffset;
				var mbHeader = MethodBodyParser.parseMethodBody(peImage.Reader, out code, out extraSections);

				if (code.Length < 6 || code[0] != 0x2A || code[1] != 0x2A)
					continue;
				dm.code = code;
				dm.extraSections = extraSections;

				int seed = BitConverter.ToInt32(code, 2);
				Array.Copy(newCodeHeader, code, newCodeHeader.Length);
				if (seed == 0)
					decrypt(code);
				else
					decrypt(code, seed);

				dm.mdImplFlags = peImage.offsetReadUInt16(methodDefOffset + (uint)methodDef.fields[1].offset);
				dm.mdFlags = peImage.offsetReadUInt16(methodDefOffset + (uint)methodDef.fields[2].offset);
				dm.mdName = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[3].offset, methodDef.fields[3].size);
				dm.mdSignature = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[4].offset, methodDef.fields[4].size);
				dm.mdParamList = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[5].offset, methodDef.fields[5].size);

				dm.mhFlags = mbHeader.flags;
				dm.mhMaxStack = mbHeader.maxStack;
				dm.mhCodeSize = (uint)dm.code.Length;
				dm.mhLocalVarSigTok = mbHeader.localVarSigTok;

				dumpedMethods.add(dm);
				decrypted = true;
			}

			return decrypted;
		}

		void decrypt(byte[] data) {
			for (int i = 6; i < data.Length; i++)
				data[i] ^= decryptKey[i % decryptKey.Length];
		}

		void decrypt(byte[] data, int seed) {
			var key = new KeyGenerator(seed).generate(data.Length);
			for (int i = 6; i < data.Length; i++)
				data[i] ^= key[i];
		}

		public void deobfuscate(Blocks blocks) {
			if (initMethod == null)
				return;
			if (blocks.Method.Name != ".cctor")
				return;

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Call)
						continue;
					var calledMethod = instr.Operand as MethodReference;
					if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(calledMethod, initMethod))
						continue;
					block.remove(i, 1);
					i--;
				}
			}
		}
	}
}
