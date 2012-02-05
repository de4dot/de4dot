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
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.code.PE;

namespace de4dot.code.deobfuscators.CodeVeil {
	// The code isn't currently encrypted at all! But let's keep this class name.
	class MethodsDecrypter {
		ModuleDefinition module;
		TypeDefinition methodsType;
		List<int> rvas;	// _stub and _executive

		public bool Detected {
			get { return methodsType != null; }
		}

		public MethodsDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public MethodsDecrypter(ModuleDefinition module, MethodsDecrypter oldOne) {
			this.module = module;
			this.methodsType = lookup(oldOne.methodsType, "Could not find methods type");
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;

			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;

				var ldci4_2 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var initMethod = call.Operand as MethodDefinition;
				if (!checkInitMethod(initMethod))
					continue;
				if (!checkMethodsType(initMethod.DeclaringType))
					continue;

				methodsType = initMethod.DeclaringType;
				break;
			}
		}

		bool checkInitMethod(MethodDefinition initMethod) {
			if (initMethod == null)
				return false;

			if (initMethod.Body == null)
				return false;
			if (!initMethod.IsStatic)
				return false;
			if (!DotNetUtils.isMethod(initMethod, "System.Void", "(System.Boolean,System.Boolean)"))
				return false;
			if (!hasCodeString(initMethod, "E_FullTrust") &&		// 4.0 / 4.1
				!hasCodeString(initMethod, "Full Trust Required"))	// 3.2
				return false;

			return true;
		}

		static bool hasCodeString(MethodDefinition method, string str) {
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				if (s == str)
					return true;
			}
			return false;
		}

		bool checkMethodsType(TypeDefinition type) {
			var fields = getRvaFields(type);
			if (fields.Count < 2)
				return false;

			rvas = new List<int>(fields.Count);
			foreach (var field in fields)
				rvas.Add(field.RVA);
			return true;
		}

		static List<FieldDefinition> getRvaFields(TypeDefinition type) {
			var fields = new List<FieldDefinition>();
			foreach (var field in type.Fields) {
				if (field.FieldType.EType != ElementType.U1 && field.FieldType.EType != ElementType.U4)
					continue;
				if (field.RVA == 0)
					continue;

				fields.Add(field);
			}
			return fields;
		}

		public bool decrypt(byte[] fileData, ref Dictionary<uint, DumpedMethod> dumpedMethods) {
			if (methodsType == null)
				return false;

			var peImage = new PeImage(fileData);
			if (peImage.Sections.Length <= 0)
				return false;

			var methodsData = findMethodsData(peImage, fileData);
			if (methodsData == null)
				return false;

			dumpedMethods = createDumpedMethods(peImage, fileData, methodsData);
			if (dumpedMethods == null)
				return false;

			return true;
		}

		Dictionary<uint, DumpedMethod> createDumpedMethods(PeImage peImage, byte[] fileData, byte[] methodsData) {
			var dumpedMethods = new Dictionary<uint, DumpedMethod>();

			var methodsDataReader = new BinaryReader(new MemoryStream(methodsData));
			var fileDataReader = new BinaryReader(new MemoryStream(fileData));

			var metadataTables = peImage.Cor20Header.createMetadataTables();
			var methodDef = metadataTables.getMetadataType(MetadataIndex.iMethodDef);
			uint methodDefOffset = methodDef.fileOffset;
			for (int i = 0; i < methodDef.rows; i++, methodDefOffset += methodDef.totalSize) {
				uint token = (uint)(0x06000001 + i);
				uint bodyRva = peImage.offsetReadUInt32(methodDefOffset);
				if (bodyRva == 0)
					continue;
				uint bodyOffset = peImage.rvaToOffset(bodyRva);

				var dm = new DumpedMethod();
				dm.mdImplFlags = peImage.offsetReadUInt16(methodDefOffset + (uint)methodDef.fields[1].offset);
				dm.mdFlags = peImage.offsetReadUInt16(methodDefOffset + (uint)methodDef.fields[2].offset);
				dm.mdName = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[3].offset, methodDef.fields[3].size);
				dm.mdSignature = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[4].offset, methodDef.fields[4].size);
				dm.mdParamList = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[5].offset, methodDef.fields[5].size);

				byte b = peImage.offsetReadByte(bodyOffset);
				uint codeOffset;
				if ((b & 3) == 2) {
					if (b != 2)
						continue;	// not zero byte code size

					dm.mhFlags = 2;
					dm.mhMaxStack = 8;
					dm.mhLocalVarSigTok = 0;
					codeOffset = bodyOffset + 1;
				}
				else {
					if (peImage.offsetReadUInt32(bodyOffset + 4) != 0)
						continue;	// not zero byte code size

					dm.mhFlags = peImage.offsetReadUInt16(bodyOffset);
					dm.mhMaxStack = peImage.offsetReadUInt16(bodyOffset + 2);
					dm.mhLocalVarSigTok = peImage.offsetReadUInt32(bodyOffset + 8);
					codeOffset = bodyOffset + (uint)(dm.mhFlags >> 12) * 4;
				}
				fileDataReader.BaseStream.Position = codeOffset;
				if (fileDataReader.ReadByte() != 0x2A)
					continue;	// Not a RET
				int methodsDataOffset = DeobUtils.readVariableLengthInteger(fileDataReader);
				methodsDataReader.BaseStream.Position = methodsDataOffset;

				dm.mhCodeSize = (uint)DeobUtils.readVariableLengthInteger(methodsDataReader);
				dm.code = methodsDataReader.ReadBytes((int)dm.mhCodeSize);
				if ((dm.mhFlags & 8) != 0)
					dm.extraSections = readExtraSections(methodsDataReader);

				dumpedMethods[token] = dm;
			}

			return dumpedMethods;
		}

		static void align(BinaryReader reader, int alignment) {
			reader.BaseStream.Position = (reader.BaseStream.Position + alignment - 1) & ~(alignment - 1);
		}

		static byte[] readExtraSections(BinaryReader reader) {
			align(reader, 4);
			int startPos = (int)reader.BaseStream.Position;
			parseSection(reader);
			int size = (int)reader.BaseStream.Position - startPos;
			reader.BaseStream.Position = startPos;
			return reader.ReadBytes(size);
		}

		static void parseSection(BinaryReader reader) {
			byte flags;
			do {
				align(reader, 4);

				flags = reader.ReadByte();
				if ((flags & 1) == 0)
					throw new ApplicationException("Not an exception section");
				if ((flags & 0x3E) != 0)
					throw new ApplicationException("Invalid bits set");

				if ((flags & 0x40) != 0) {
					reader.BaseStream.Position--;
					int num = (int)(reader.ReadUInt32() >> 8) / 24;
					reader.BaseStream.Position += num * 24;
				}
				else {
					int num = reader.ReadByte() / 12;
					reader.BaseStream.Position += 2 + num * 12;
				}
			} while ((flags & 0x80) != 0);
		}

		// xor eax, eax / inc eax / pop esi edi edx ecx ebx / leave / ret 0Ch
		static byte[] initializeMethodEnd = new byte[] {
			0x33, 0xC0, 0x40, 0x5E, 0x5F, 0x5A, 0x59, 0x5B, 0xC9, 0xC2, 0x0C, 0x00,
		};
		byte[] findMethodsData(PeImage peImage, byte[] fileData) {
			var section = peImage.Sections[0];

			var reader = new BinaryReader(new MemoryStream(fileData));

			const int RVA_EXECUTIVE_OFFSET = 1 * 4;
			const int ENC_CODE_OFFSET = 6 * 4;
			for (int offset = 0; offset < section.sizeOfRawData - (ENC_CODE_OFFSET + 4 - 1); ) {
				offset = findSig(fileData, offset, initializeMethodEnd);
				if (offset < 0)
					return null;
				offset += initializeMethodEnd.Length;

				int rva = BitConverter.ToInt32(fileData, offset + RVA_EXECUTIVE_OFFSET);
				if (rvas.IndexOf(rva) < 0)
					continue;

				int relOffs = BitConverter.ToInt32(fileData, offset + ENC_CODE_OFFSET);
				if (relOffs < 0 || relOffs >= section.sizeOfRawData)
					continue;
				reader.BaseStream.Position = section.pointerToRawData + relOffs;

				int size = DeobUtils.readVariableLengthInteger(reader);
				int endOffset = relOffs + size;
				if (endOffset < relOffs || endOffset > section.sizeOfRawData)
					continue;

				return reader.ReadBytes(size);
			}

			return null;
		}

		static int findSig(byte[] fileData, int offset, byte[] sig) {
			for (int i = offset; i < fileData.Length - sig.Length + 1; i++) {
				if (fileData[i] != sig[0])
					continue;
				if (compare(fileData, i + 1, sig, 1, sig.Length - 1))
					return i;
			}
			return -1;
		}

		static bool compare(byte[] a1, int i1, byte[] a2, int i2, int len) {
			for (int i = 0; i < len; i++) {
				if (a1[i1++] != a2[i2++])
					return false;
			}
			return true;
		}
	}
}
