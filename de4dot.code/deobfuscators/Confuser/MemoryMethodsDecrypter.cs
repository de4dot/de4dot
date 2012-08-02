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
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v14_r57884,
			v14_r58004,
			v14_r58564,
			v15a_r59014,
			v16_r71742,
			vXX,
		}

		public MemoryMethodsDecrypter(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module, simpleDeobfuscator) {
		}

		public MemoryMethodsDecrypter(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, MemoryMethodsDecrypter other)
			: base(module, simpleDeobfuscator, other) {
		}

		protected override bool checkType(TypeDefinition type, MethodDefinition initMethod) {
			if (type == null)
				return false;
			if (type.Methods.Count != 3)
				return false;
			var virtProtect = DotNetUtils.getPInvokeMethod(type, "kernel32", "VirtualProtect");
			if (virtProtect == null)
				return false;
			if (!DotNetUtils.hasString(initMethod, "Broken file"))
				return false;

			if ((decryptMethod = findDecryptMethod(type)) == null)
				return false;

			bool callsFileStreamCtor = DotNetUtils.callsMethod(initMethod, "System.Void System.IO.FileStream::.ctor(System.String,System.IO.FileMode,System.IO.FileAccess,System.IO.FileShare)");
			if (!DotNetUtils.hasString(initMethod, "Module error"))
				version = ConfuserVersion.v14_r57884;
			else if (virtProtect.IsPrivate && callsFileStreamCtor) {
				int calls = countMethodCalls(initMethod, "System.Void System.Buffer::BlockCopy(System.Array,System.Int32,System.Array,System.Int32,System.Int32)");
				if (calls <= 2)
					version = ConfuserVersion.v14_r58564;
				else if (calls == 4)
					version = ConfuserVersion.v15a_r59014;
				else
					return false;
			}
			else if (callsFileStreamCtor)
				version = ConfuserVersion.v14_r58004;
			else if (DotNetUtils.callsMethod(initMethod, "System.Int32 System.Object::GetHashCode()"))
				version = ConfuserVersion.v16_r71742;
			else
				version = ConfuserVersion.vXX;

			return true;
		}

		static int countMethodCalls(MethodDefinition method, string methodFullName) {
			if (method == null || method.Body == null)
				return 0;
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt && instr.OpCode.Code != Code.Newobj)
					continue;
				var calledMethod = instr.Operand as MethodReference;
				if (calledMethod != null && calledMethod.FullName == methodFullName)
					count++;
			}
			return count;
		}

		public void initialize() {
			if (initMethod == null)
				return;

			if (!initializeKeys())
				throw new ApplicationException("Could not find all decryption keys");
		}

		bool initializeKeys() {
			switch (version) {
			case ConfuserVersion.v14_r57884:
			case ConfuserVersion.v14_r58004:
				return true;

			case ConfuserVersion.v14_r58564:
			case ConfuserVersion.v15a_r59014:
				return initializeKeys_v14_r58564();

			case ConfuserVersion.v16_r71742:
				return initializeKeys_v16_r71742();

			case ConfuserVersion.vXX:
				return initializeKeys_vXX();

			default:
				throw new ApplicationException("Unknown version");
			}
		}

		bool initializeKeys_v14_r58564() {
			simpleDeobfuscator.deobfuscate(initMethod);
			if (!findLKey0(initMethod, out lkey0))
				return false;
			if (!findKey0_v14_r58564(initMethod, out key0))
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

		bool initializeKeys_v16_r71742() {
			simpleDeobfuscator.deobfuscate(initMethod);
			if (!findLKey0(initMethod, out lkey0))
				return false;
			if (!findKey0_v16_r71742(initMethod, out key0))
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

		bool initializeKeys_vXX() {
			simpleDeobfuscator.deobfuscate(initMethod);
			if (!findLKey0(initMethod, out lkey0))
				return false;
			if (!findKey0_v16_r71742(initMethod, out key0))
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

		public bool decrypt(PeImage peImage, byte[] fileData) {
			if (initMethod == null)
				return false;

			switch (version) {
			case ConfuserVersion.v14_r57884: return decrypt_v14_r57884(peImage, fileData);
			case ConfuserVersion.v14_r58004: return decrypt_v14_r58004(peImage, fileData);
			case ConfuserVersion.v14_r58564: return decrypt_v14_r58004(peImage, fileData);
			case ConfuserVersion.v15a_r59014:return decrypt_v15a_r59014(peImage, fileData);
			case ConfuserVersion.v16_r71742: return decrypt_v16_r71742(peImage, fileData);
			case ConfuserVersion.vXX: return decrypt_vXX(peImage, fileData);
			default: throw new ApplicationException("Unknown version");
			}
		}

		bool decrypt_v14_r57884(PeImage peImage, byte[] fileData) {
			methodsData = decryptMethodsData_v14_r57884(peImage, false);

			var reader = new BinaryReader(new MemoryStream(methodsData));
			reader.ReadInt16();	// sig
			var writer = new BinaryWriter(new MemoryStream(fileData));
			int numInfos = reader.ReadInt32();
			for (int i = 0; i < numInfos; i++) {
				uint rva = reader.ReadUInt32();
				if (rva == 0)
					continue;
				writer.BaseStream.Position = peImage.rvaToOffset(rva);
				writer.Write(reader.ReadBytes(reader.ReadInt32()));
			}

			return true;
		}

		byte[] decryptMethodsData_v14_r57884(PeImage peImage, bool hasStrongNameInfo) {
			var reader = peImage.Reader;
			reader.BaseStream.Position = 0;
			var md5SumData = reader.ReadBytes((int)peImage.OptionalHeader.checkSum ^ (int)key0);

			int csOffs = (int)peImage.OptionalHeader.Offset + 0x40;
			Array.Clear(md5SumData, csOffs, 4);
			var md5Sum = DeobUtils.md5Sum(md5SumData);
			ulong checkSum = reader.ReadUInt64() ^ lkey0;
			if (hasStrongNameInfo) {
				int sn = reader.ReadInt32();
				int snLen = reader.ReadInt32();
				if (sn != 0) {
					if (peImage.rvaToOffset(peImage.Cor20Header.strongNameSignature.virtualAddress) != sn ||
						peImage.Cor20Header.strongNameSignature.size != snLen)
						throw new ApplicationException("Invalid sn and snLen");
					Array.Clear(md5SumData, sn, snLen);
				}
			}
			if (checkSum != calcChecksum(md5SumData))
				throw new ApplicationException("Invalid checksum. File has been modified.");
			var iv = reader.ReadBytes(reader.ReadInt32() ^ (int)key2);
			var encrypted = reader.ReadBytes(reader.ReadInt32() ^ (int)key3);
			var decrypted = decrypt(encrypted, iv, md5SumData);
			if (BitConverter.ToInt16(decrypted, 0) != 0x6FD6)
				throw new ApplicationException("Invalid magic");
			return decrypted;
		}

		bool decrypt_v14_r58004(PeImage peImage, byte[] fileData) {
			methodsData = decryptMethodsData_v14_r57884(peImage, false);
			return decryptImage_v14_r58004(peImage, fileData);
		}

		bool decryptImage_v14_r58004(PeImage peImage, byte[] fileData) {
			var reader = new BinaryReader(new MemoryStream(methodsData));
			reader.ReadInt16();	// sig
			var writer = new BinaryWriter(new MemoryStream(fileData));
			int numInfos = reader.ReadInt32();
			for (int i = 0; i < numInfos; i++) {
				uint offs = reader.ReadUInt32() ^ key4;
				if (offs == 0)
					continue;
				uint rva = reader.ReadUInt32() ^ key4;
				if (peImage.rvaToOffset(rva) != offs)
					throw new ApplicationException("Invalid offs & rva");
				writer.BaseStream.Position = peImage.rvaToOffset(rva);
				writer.Write(reader.ReadBytes(reader.ReadInt32()));
			}

			return true;
		}

		bool decrypt_v15a_r59014(PeImage peImage, byte[] fileData) {
			methodsData = decryptMethodsData_v14_r57884(peImage, true);
			return decryptImage_v14_r58004(peImage, fileData);
		}

		bool decrypt_v16_r71742(PeImage peImage, byte[] fileData) {
			methodsData = decryptMethodsData_v16_r71742(peImage, getEncryptedHeaderOffset_v16_r71742(peImage.Sections));
			return decryptImage_v16_r71742(peImage, fileData);
		}

		bool decrypt_vXX(PeImage peImage, byte[] fileData) {
			if (peImage.OptionalHeader.checkSum == 0)
				return false;

			methodsData = decryptMethodsData_v17_r73404(peImage);
			return decryptImage_v16_r71742(peImage, fileData);
		}

		bool decryptImage_v16_r71742(PeImage peImage, byte[] fileData) {
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
