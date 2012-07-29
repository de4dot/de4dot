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
using Mono.Cecil.Cil;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.Confuser {
	class MemoryMethodsDecrypter : MethodsDecrypterBase {
		public MemoryMethodsDecrypter(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module, simpleDeobfuscator) {
		}

		public MemoryMethodsDecrypter(ModuleDefinition module, MemoryMethodsDecrypter other)
			: base(module, other) {
		}

		protected override bool checkType(TypeDefinition type, MethodDefinition initMethod) {
			if (type == null)
				return false;
			if (type.Methods.Count != 3)
				return false;
			if (DotNetUtils.getPInvokeMethod(type, "kernel32", "VirtualProtect") == null)
				return false;
			if (!DotNetUtils.hasString(initMethod, "Module error"))
				return false;
			if (!DotNetUtils.hasString(initMethod, "Broken file"))
				return false;

			if ((decryptMethod = findDecryptMethod(type)) == null)
				return false;

			return true;
		}

		public void initialize() {
			if (initMethod == null)
				return;
			if (!initializeKeys())
				throw new ApplicationException("Could not find all decryption keys");
		}

		bool initializeKeys() {
			simpleDeobfuscator.deobfuscate(initMethod);
			if (!findLKey0(initMethod, out lkey0))
				return false;
			if (!findKey0(initMethod, out key0))
				return false;
			if (!findKey1(initMethod, out key1))
				return false;
			if (!findKey2Key3(initMethod, out key2, out key3))
				return false;
			if (!findKey4(initMethod, out key4))
				return false;
			if (!findKey5(initMethod, out key5))
				return false;

			simpleDeobfuscator.deobfuscate(decryptMethod);
			if (!findKey6(decryptMethod, out key6))
				return false;

			return true;
		}

		static bool findKey4(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = findCallvirtReadUInt32(instrs, i);
				if (i < 0)
					break;
				if (i >= 2) {
					if (instrs[i-2].OpCode.Code == Code.Pop)
						continue;
				}
				if (i + 4 >= instrs.Count)
					break;

				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				var stloc = instrs[i + 3];
				if (!DotNetUtils.isStloc(stloc))
					continue;
				var ldloc = instrs[i + 4];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				if (DotNetUtils.getLocalVar(method.Body.Variables, ldloc) != DotNetUtils.getLocalVar(method.Body.Variables, stloc))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			key = 0;
			return false;
		}

		static bool findKey5(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = findCallvirtReadUInt32(instrs, i);
				if (i < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.IO.BinaryReader::ReadInt32()");
				if (index2 < 0)
					break;
				if (index2 - i != 6)
					continue;

				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				var stloc = instrs[i + 3];
				if (!DotNetUtils.isStloc(stloc))
					continue;
				var ldloc = instrs[i + 4];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				if (DotNetUtils.getLocalVar(method.Body.Variables, ldloc) == DotNetUtils.getLocalVar(method.Body.Variables, stloc))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 5]))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			key = 0;
			return false;
		}

		public bool decrypt(PeImage peImage, byte[] fileData, ref DumpedMethods dumpedMethods) {
			if (initMethod == null)
				return false;
			if (peImage.OptionalHeader.checkSum == 0)
				return false;

			methodsData = decryptMethodsData(peImage);
			return decrypt(peImage, fileData);
		}

		bool decrypt(PeImage peImage, byte[] fileData) {
			var reader = new BinaryReader(new MemoryStream(methodsData));
			reader.ReadInt16();	// sig
			int numInfos = reader.ReadInt32();
			for (int i = 0; i < numInfos; i++) {
				uint offs = reader.ReadUInt32() ^ key4;
				if (offs == 0)
					continue;
				uint rva = reader.ReadUInt32() ^ key5;
				if (peImage.rvaToOffset(rva) != offs)
					throw new ApplicationException("Invalid offs & rva");
				int len = reader.ReadInt32();
				for (int j = 0; j < len; j++)
					fileData[offs + j] = reader.ReadByte();
			}
			return true;
		}
	}
}
