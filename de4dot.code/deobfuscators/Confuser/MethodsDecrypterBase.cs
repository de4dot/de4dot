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
using System.Security.Cryptography;
using dnlib.PE;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	abstract class MethodsDecrypterBase : IVersionProvider {
		protected ModuleDefMD module;
		protected ISimpleDeobfuscator simpleDeobfuscator;
		protected MethodDef initMethod;
		protected MethodDef decryptMethod;
		protected ulong lkey0;
		protected uint key0, key1, key2, key3, key4, key5, key6;
		protected byte[] methodsData;

		public MethodDef InitMethod {
			get { return initMethod; }
		}

		public TypeDef Type {
			get { return initMethod != null ? initMethod.DeclaringType : null; }
		}

		public bool Detected {
			get { return initMethod != null; }
		}

		protected MethodsDecrypterBase(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		protected MethodsDecrypterBase(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, MethodsDecrypterBase other) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
			if (other != null)
				this.initMethod = lookup(other.initMethod, "Could not find initMethod");
		}

		T lookup<T>(T def, string errorMessage) where T : class, ICodedToken {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public abstract bool getRevisionRange(out int minRev, out int maxRev);

		public void find() {
			find(DotNetUtils.getModuleTypeCctor(module));
		}

		bool find(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				try {
					// If the body is encrypted, this could throw
					if (calledMethod == null || calledMethod.Body == null)
						continue;
				}
				catch {
					continue;
				}
				if (!DotNetUtils.isMethod(calledMethod, "System.Void", "()"))
					continue;
				if (!checkType(calledMethod.DeclaringType, calledMethod))
					continue;

				initMethod = calledMethod;
				return true;
			}
			return false;
		}

		protected abstract bool checkType(TypeDef type, MethodDef initMethod);

		protected static MethodDef findDecryptMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte[],System.Byte[],System.Byte[])"))
					continue;

				return method;
			}
			return null;
		}

		protected static bool findLKey0(MethodDef method, out ulong key) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = findCallvirtReadUInt64(instrs, index);
				if (index < 0)
					break;
				if (index + 1 >= instrs.Count)
					continue;
				var ldci8 = instrs[index + 1];
				if (ldci8.OpCode.Code != Code.Ldc_I8)
					continue;

				key = (ulong)(long)ldci8.Operand;
				return true;
			}

			key = 0;
			return false;
		}

		protected static bool findKey0_v16_r71742(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i + 5 < instrs.Count; i++) {
				i = findCallvirtReadUInt32(instrs, i);
				if (i < 0)
					break;

				int index = i + 1;
				var ldci4_1 = instrs[index++];
				if (!ldci4_1.IsLdcI4())
					continue;
				if (instrs[index++].OpCode.Code != Code.Xor)
					continue;
				if (!instrs[index++].IsStloc())
					continue;
				if (!instrs[index++].IsLdloc())
					continue;
				var ldci4_2 = instrs[index++];
				if (!ldci4_2.IsLdcI4())
					continue;
				if (ldci4_1.GetLdcI4Value() != ldci4_2.GetLdcI4Value())
					continue;

				key = (uint)ldci4_1.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		protected static bool findKey0_v14_r58564(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i + 5 < instrs.Count; i++) {
				i = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.IO.BinaryReader::ReadInt32()");
				if (i < 0)
					break;

				int index = i + 1;
				var ldci4_1 = instrs[index++];
				if (!ldci4_1.IsLdcI4())
					continue;
				if (instrs[index++].OpCode.Code != Code.Xor)
					continue;
				if (!instrs[index++].IsStloc())
					continue;
				if (!instrs[index++].IsLdloc())
					continue;
				var ldci4_2 = instrs[index++];
				if (!ldci4_2.IsLdcI4())
					continue;
				if (ldci4_2.GetLdcI4Value() != 0 && ldci4_1.GetLdcI4Value() != ldci4_2.GetLdcI4Value())
					continue;

				key = (uint)ldci4_1.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		protected static bool findKey1(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = findCallvirtReadUInt32(instrs, index);
				if (index < 0)
					break;
				if (index == 0)
					continue;
				int i = index - 1;
				if (!checkCallvirtReadUInt32(instrs, ref i))
					continue;
				if (!checkCallvirtReadUInt32(instrs, ref i))
					continue;
				if (!checkCallvirtReadUInt32(instrs, ref i))
					continue;
				if (!checkCallvirtReadUInt32(instrs, ref i))
					continue;

				if (i + 1 >= instrs.Count)
					continue;
				if (!instrs[i].IsLdloc())
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		static bool checkCallvirtReadUInt32(IList<Instruction> instrs, ref int index) {
			if (index + 2 >= instrs.Count)
				return false;

			if (!instrs[index].IsLdloc())
				return false;
			if (!ConfuserUtils.isCallMethod(instrs[index + 1], Code.Callvirt, "System.UInt32 System.IO.BinaryReader::ReadUInt32()"))
				return false;
			if (!instrs[index + 2].IsStloc() && instrs[index + 2].OpCode.Code != Code.Pop)
				return false;

			index += 3;
			return true;
		}

		protected static bool findKey2Key3(MethodDef method, out uint key2, out uint key3) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = i;
				if (!findKey2OrKey3(instrs, ref index, out key2))
					continue;
				if (!findKey2OrKey3(instrs, ref index, out key3))
					continue;

				return true;
			}

			key2 = 0;
			key3 = 0;
			return false;
		}

		static bool findKey2OrKey3(IList<Instruction> instrs, ref int index, out uint key) {
			key = 0;
			if (index + 6 >= instrs.Count)
				return false;
			int i = index;
			if (!instrs[i++].IsLdloc())
				return false;
			if (!instrs[i++].IsLdloc())
				return false;
			if (!ConfuserUtils.isCallMethod(instrs[i++], Code.Callvirt, "System.Int32 System.IO.BinaryReader::ReadInt32()"))
				return false;
			var ldci4 = instrs[i++];
			if (!ldci4.IsLdcI4())
				return false;
			if (instrs[i++].OpCode.Code != Code.Xor)
				return false;
			if (!ConfuserUtils.isCallMethod(instrs[i++], Code.Callvirt, "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)"))
				return false;
			if (!instrs[i++].IsStloc())
				return false;

			key = (uint)ldci4.GetLdcI4Value();
			index = i;
			return true;
		}

		protected static bool findKey6(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i + 4 < instrs.Count; i++) {
				int index = i;
				if (!instrs[index++].IsLdloc())
					continue;
				if (instrs[index++].OpCode.Code != Code.Sub)
					continue;
				if (instrs[index++].OpCode.Code != Code.Ldelem_U1)
					continue;
				var ldci4 = instrs[index++];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index++].OpCode.Code != Code.Xor)
					continue;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		protected static int findCallvirtReadUInt32(IList<Instruction> instrs, int index) {
			return ConfuserUtils.findCallMethod(instrs, index, Code.Callvirt, "System.UInt32 System.IO.BinaryReader::ReadUInt32()");
		}

		static int findCallvirtReadUInt64(IList<Instruction> instrs, int index) {
			return ConfuserUtils.findCallMethod(instrs, index, Code.Callvirt, "System.UInt64 System.IO.BinaryReader::ReadUInt64()");
		}

		protected byte[] decryptMethodsData_v17_r73404(MyPEImage peImage) {
			return decryptMethodsData_v16_r71742(peImage, getEncryptedHeaderOffset_vXX(peImage.Sections));
		}

		protected byte[] decryptMethodsData_v16_r71742(MyPEImage peImage, uint encryptedHeaderOffset) {
			uint mdRva = peImage.OptionalHeader.CheckSum ^ (uint)key0;
			if ((RVA)mdRva != peImage.Cor20Header.MetaData.VirtualAddress)
				throw new ApplicationException("Invalid metadata rva");
			var reader = peImage.Reader;
			reader.Position = encryptedHeaderOffset;
			ulong checkSum = reader.ReadUInt64() ^ lkey0;
			reader.ReadInt32();	// strong name RVA
			reader.ReadInt32();	// strong name len
			var iv = reader.ReadBytes(reader.ReadInt32() ^ (int)key2);
			var encrypted = reader.ReadBytes(reader.ReadInt32() ^ (int)key3);
			var streamsBuffer = getStreamsBuffer(peImage);
			if (checkSum != calcChecksum(streamsBuffer))
				throw new ApplicationException("Invalid checksum. File has been modified.");
			var decrypted = decrypt(encrypted, iv, streamsBuffer);
			if (BitConverter.ToInt16(decrypted, 0) != 0x6FD6)
				throw new ApplicationException("Invalid magic");
			return decrypted;
		}

		protected uint getEncryptedHeaderOffset_v16_r71742(IList<ImageSectionHeader> sections) {
			for (int i = sections.Count - 1; i >= 0; i--) {
				var section = sections[i];
				if (section.DisplayName == ".confuse")
					return section.PointerToRawData;
			}
			throw new ApplicationException("Could not find encrypted section");
		}

		uint getEncryptedHeaderOffset_vXX(IList<ImageSectionHeader> sections) {
			for (int i = sections.Count - 1; i >= 0; i--) {
				var section = sections[i];
				if (getSectionNameHash(section) == (uint)key1)
					return section.PointerToRawData;
			}
			throw new ApplicationException("Could not find encrypted section");
		}

		static byte[] getStreamsBuffer(MyPEImage peImage) {
			var memStream = new MemoryStream();
			var writer = new BinaryWriter(memStream);
			var reader = peImage.Reader;
			foreach (var mdStream in peImage.DotNetFile.MetaData.AllStreams) {
				reader.Position = (long)mdStream.StartOffset;
				writer.Write(reader.ReadBytes((int)(mdStream.EndOffset - mdStream.StartOffset)));
			}
			return memStream.ToArray();
		}

		protected static ulong calcChecksum(byte[] data) {
			var sum = DeobUtils.md5Sum(data);
			return BitConverter.ToUInt64(sum, 0) ^ BitConverter.ToUInt64(sum, 8);
		}

		static uint getSectionNameHash(ImageSectionHeader section) {
			uint hash = 0;
			foreach (var c in section.Name)
				hash += c;
			return hash;
		}

		protected byte[] decrypt(byte[] encrypted, byte[] iv, byte[] streamsBuffer) {
			var decrypted = DeobUtils.aesDecrypt(encrypted, DeobUtils.sha256Sum(streamsBuffer), iv);
			var sha = SHA512.Create();
			var hash = sha.ComputeHash(streamsBuffer);
			for (int i = 0; i < decrypted.Length; i += 64) {
				int j;
				for (j = 0; j < 64 && i + j < decrypted.Length; j++)
					decrypted[i + j] ^= (byte)(hash[j] ^ key6);
				hash = sha.ComputeHash(decrypted, i, j);
			}
			return decrypted;
		}
	}
}
