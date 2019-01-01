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
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class MemoryMethodsDecrypter : MethodsDecrypterBase {
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v14_r57884,
			v14_r58004,
			v14_r58564,
			v14_r58852,
			v15_r59014,
			v16_r71742,
			v17_r72989,
			// Removed in Confuser 1.7 r73404 and restored in Confuser 1.7 r73605
			v17_r73605,
			v18_r75288,
			v19_r75725,
		}

		public MemoryMethodsDecrypter(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module, simpleDeobfuscator) {
		}

		public MemoryMethodsDecrypter(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, MemoryMethodsDecrypter other)
			: base(module, simpleDeobfuscator, other) {
			if (other != null)
				version = other.version;
		}

		protected override bool CheckType(TypeDef type, MethodDef initMethod) {
			if (type == null)
				return false;
			if (type.Methods.Count != 3)
				return false;
			var virtProtect = DotNetUtils.GetPInvokeMethod(type, "kernel32", "VirtualProtect");
			if (virtProtect == null)
				return false;
			if (!DotNetUtils.HasString(initMethod, "Broken file"))
				return false;

			if ((decryptMethod = FindDecryptMethod(type)) == null)
				return false;

			bool callsFileStreamCtor = DotNetUtils.CallsMethod(initMethod, "System.Void System.IO.FileStream::.ctor(System.String,System.IO.FileMode,System.IO.FileAccess,System.IO.FileShare)");
			if (!DotNetUtils.HasString(initMethod, "Module error"))
				version = ConfuserVersion.v14_r57884;
			else if (virtProtect.IsPrivate && callsFileStreamCtor) {
				int calls = ConfuserUtils.CountCalls(initMethod, "System.Void System.Buffer::BlockCopy(System.Array,System.Int32,System.Array,System.Int32,System.Int32)");
				if (calls <= 1)
					version = ConfuserVersion.v14_r58564;
				else if (calls == 2)
					version = ConfuserVersion.v14_r58852;
				else if (calls == 4)
					version = ConfuserVersion.v15_r59014;
				else
					return false;
			}
			else if (callsFileStreamCtor)
				version = ConfuserVersion.v14_r58004;
			else if (DotNetUtils.CallsMethod(initMethod, "System.Int32 System.Object::GetHashCode()")) {
				if (DotNetUtils.HasString(initMethod, "<Unknown>"))
					version = ConfuserVersion.v17_r72989;
				else
					version = ConfuserVersion.v16_r71742;
			}
			else if (DotNetUtils.CallsMethod(decryptMethod, "System.Security.Cryptography.Rijndael System.Security.Cryptography.Rijndael::Create()"))
				version = ConfuserVersion.v17_r73605;
			else if (DotNetUtils.HasString(initMethod, "<Unknown>"))
				version = ConfuserVersion.v18_r75288;
			else
				version = ConfuserVersion.v19_r75725;

			return true;
		}

		public void Initialize() {
			if (initMethod == null)
				return;

			if (!InitializeKeys())
				throw new ApplicationException("Could not find all decryption keys");
		}

		bool InitializeKeys() {
			switch (version) {
			case ConfuserVersion.v14_r57884:
			case ConfuserVersion.v14_r58004:
				return true;

			case ConfuserVersion.v14_r58564:
			case ConfuserVersion.v14_r58852:
			case ConfuserVersion.v15_r59014:
				return InitializeKeys_v14_r58564();

			case ConfuserVersion.v16_r71742:
			case ConfuserVersion.v17_r72989:
				return InitializeKeys_v16_r71742();

			case ConfuserVersion.v17_r73605:
			case ConfuserVersion.v18_r75288:
			case ConfuserVersion.v19_r75725:
				return InitializeKeys_v17_r73605();

			default:
				throw new ApplicationException("Unknown version");
			}
		}

		bool InitializeKeys_v14_r58564() {
			simpleDeobfuscator.Deobfuscate(initMethod);
			if (!FindLKey0(initMethod, out lkey0))
				return false;
			if (!FindKey0_v14_r58564(initMethod, out key0))
				return false;
			if (!FindKey2Key3(initMethod, out key2, out key3))
				return false;
			if (!FindKey4(initMethod, out key4))
				return false;
			if (!FindKey5(initMethod, out key5))
				return false;

			simpleDeobfuscator.Deobfuscate(decryptMethod);
			if (!FindKey6(decryptMethod, out key6))
				return false;

			return true;
		}

		bool InitializeKeys_v16_r71742() {
			simpleDeobfuscator.Deobfuscate(initMethod);
			if (!FindLKey0(initMethod, out lkey0))
				return false;
			if (!FindKey0_v16_r71742(initMethod, out key0))
				return false;
			if (!FindKey2Key3(initMethod, out key2, out key3))
				return false;
			if (!FindKey4(initMethod, out key4))
				return false;
			if (!FindKey5(initMethod, out key5))
				return false;

			simpleDeobfuscator.Deobfuscate(decryptMethod);
			if (!FindKey6(decryptMethod, out key6))
				return false;

			return true;
		}

		bool InitializeKeys_v17_r73605() {
			simpleDeobfuscator.Deobfuscate(initMethod);
			if (!FindLKey0(initMethod, out lkey0))
				return false;
			if (!FindKey0_v16_r71742(initMethod, out key0))
				return false;
			if (!FindKey1(initMethod, out key1))
				return false;
			if (!FindKey2Key3(initMethod, out key2, out key3))
				return false;
			if (!FindKey4(initMethod, out key4))
				return false;
			if (!FindKey5(initMethod, out key5))
				return false;

			simpleDeobfuscator.Deobfuscate(decryptMethod);
			if (!FindKey6(decryptMethod, out key6))
				return false;

			return true;
		}

		static bool FindKey4(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = FindCallvirtReadUInt32(instrs, i);
				if (i < 0)
					break;
				if (i >= 2) {
					if (instrs[i-2].OpCode.Code == Code.Pop)
						continue;
				}
				if (i + 4 >= instrs.Count)
					break;

				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				var stloc = instrs[i + 3];
				if (!stloc.IsStloc())
					continue;
				var ldloc = instrs[i + 4];
				if (!ldloc.IsLdloc())
					continue;
				if (ldloc.GetLocal(method.Body.Variables) != stloc.GetLocal(method.Body.Variables))
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		static bool FindKey5(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = FindCallvirtReadUInt32(instrs, i);
				if (i < 0)
					break;
				int index2 = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.IO.BinaryReader::ReadInt32()");
				if (index2 < 0)
					break;
				if (index2 - i != 6)
					continue;

				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				var stloc = instrs[i + 3];
				if (!stloc.IsStloc())
					continue;
				var ldloc = instrs[i + 4];
				if (!ldloc.IsLdloc())
					continue;
				if (ldloc.GetLocal(method.Body.Variables) == stloc.GetLocal(method.Body.Variables))
					continue;
				if (!instrs[i + 5].IsLdloc())
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		public bool Decrypt(MyPEImage peImage, byte[] fileData) {
			if (initMethod == null)
				return false;

			switch (version) {
			case ConfuserVersion.v14_r57884: return Decrypt_v14_r57884(peImage, fileData);
			case ConfuserVersion.v14_r58004: return Decrypt_v14_r58004(peImage, fileData);
			case ConfuserVersion.v14_r58564: return Decrypt_v14_r58004(peImage, fileData);
			case ConfuserVersion.v14_r58852: return Decrypt_v14_r58004(peImage, fileData);
			case ConfuserVersion.v15_r59014: return Decrypt_v15_r59014(peImage, fileData);
			case ConfuserVersion.v16_r71742: return Decrypt_v16_r71742(peImage, fileData);
			case ConfuserVersion.v17_r72989: return Decrypt_v16_r71742(peImage, fileData);
			case ConfuserVersion.v17_r73605: return Decrypt_v17_r73605(peImage, fileData);
			case ConfuserVersion.v18_r75288: return Decrypt_v17_r73605(peImage, fileData);
			case ConfuserVersion.v19_r75725: return Decrypt_v17_r73605(peImage, fileData);
			default: throw new ApplicationException("Unknown version");
			}
		}

		bool Decrypt_v14_r57884(MyPEImage peImage, byte[] fileData) {
			methodsData = DecryptMethodsData_v14_r57884(peImage, false);

			var reader = new BinaryReader(new MemoryStream(methodsData));
			reader.ReadInt16();	// sig
			var writer = new BinaryWriter(new MemoryStream(fileData));
			int numInfos = reader.ReadInt32();
			for (int i = 0; i < numInfos; i++) {
				uint rva = reader.ReadUInt32();
				if (rva == 0)
					continue;
				writer.BaseStream.Position = peImage.RvaToOffset(rva);
				writer.Write(reader.ReadBytes(reader.ReadInt32()));
			}

			return true;
		}

		byte[] DecryptMethodsData_v14_r57884(MyPEImage peImage, bool hasStrongNameInfo) {
			var reader = peImage.Reader;
			reader.Position = 0;
			var md5SumData = reader.ReadBytes((int)peImage.OptionalHeader.CheckSum ^ (int)key0);

			int csOffs = (int)peImage.OptionalHeader.StartOffset + 0x40;
			Array.Clear(md5SumData, csOffs, 4);
			/*var md5Sum =*/ DeobUtils.Md5Sum(md5SumData);
			ulong checkSum = reader.ReadUInt64() ^ lkey0;
			if (hasStrongNameInfo) {
				int sn = reader.ReadInt32();
				int snLen = reader.ReadInt32();
				if (sn != 0) {
					if (peImage.RvaToOffset((uint)peImage.Cor20Header.StrongNameSignature.VirtualAddress) != sn ||
						peImage.Cor20Header.StrongNameSignature.Size != snLen)
						throw new ApplicationException("Invalid sn and snLen");
					Array.Clear(md5SumData, sn, snLen);
				}
			}
			if (checkSum != CalcChecksum(md5SumData))
				throw new ApplicationException("Invalid checksum. File has been modified.");
			var iv = reader.ReadBytes(reader.ReadInt32() ^ (int)key2);
			var encrypted = reader.ReadBytes(reader.ReadInt32() ^ (int)key3);
			var decrypted = Decrypt(encrypted, iv, md5SumData);
			if (BitConverter.ToInt16(decrypted, 0) != 0x6FD6)
				throw new ApplicationException("Invalid magic");
			return decrypted;
		}

		bool Decrypt_v14_r58004(MyPEImage peImage, byte[] fileData) {
			methodsData = DecryptMethodsData_v14_r57884(peImage, false);
			return DecryptImage_v14_r58004(peImage, fileData);
		}

		bool DecryptImage_v14_r58004(MyPEImage peImage, byte[] fileData) {
			var reader = new BinaryReader(new MemoryStream(methodsData));
			reader.ReadInt16();	// sig
			var writer = new BinaryWriter(new MemoryStream(fileData));
			int numInfos = reader.ReadInt32();
			for (int i = 0; i < numInfos; i++) {
				uint offs = reader.ReadUInt32() ^ key4;
				if (offs == 0)
					continue;
				uint rva = reader.ReadUInt32() ^ key4;
				if (peImage.RvaToOffset(rva) != offs)
					throw new ApplicationException("Invalid offs & rva");
				writer.BaseStream.Position = peImage.RvaToOffset(rva);
				writer.Write(reader.ReadBytes(reader.ReadInt32()));
			}

			return true;
		}

		bool Decrypt_v15_r59014(MyPEImage peImage, byte[] fileData) {
			methodsData = DecryptMethodsData_v14_r57884(peImage, true);
			return DecryptImage_v14_r58004(peImage, fileData);
		}

		bool Decrypt_v16_r71742(MyPEImage peImage, byte[] fileData) {
			methodsData = DecryptMethodsData_v16_r71742(peImage, GetEncryptedHeaderOffset_v16_r71742(peImage.Sections));
			return DecryptImage_v16_r71742(peImage, fileData);
		}

		bool Decrypt_v17_r73605(MyPEImage peImage, byte[] fileData) {
			if (peImage.OptionalHeader.CheckSum == 0)
				return false;

			methodsData = DecryptMethodsData_v17_r73404(peImage);
			return DecryptImage_v16_r71742(peImage, fileData);
		}

		bool DecryptImage_v16_r71742(MyPEImage peImage, byte[] fileData) {
			var reader = new BinaryReader(new MemoryStream(methodsData));
			reader.ReadInt16();	// sig
			int numInfos = reader.ReadInt32();
			for (int i = 0; i < numInfos; i++) {
				uint offs = reader.ReadUInt32() ^ key4;
				if (offs == 0)
					continue;
				uint rva = reader.ReadUInt32() ^ key5;
				if (peImage.RvaToOffset(rva) != offs)
					throw new ApplicationException("Invalid offs & rva");
				int len = reader.ReadInt32();
				for (int j = 0; j < len; j++)
					fileData[offs + j] = reader.ReadByte();
			}
			return true;
		}

		public override bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v14_r57884:
				minRev = 57884;
				maxRev = 57884;
				return true;

			case ConfuserVersion.v14_r58004:
				minRev = 58004;
				maxRev = 58446;
				return true;

			case ConfuserVersion.v14_r58564:
				minRev = 58564;
				maxRev = 58817;
				return true;

			case ConfuserVersion.v14_r58852:
				minRev = 58852;
				maxRev = 58919;
				return true;

			case ConfuserVersion.v15_r59014:
				minRev = 59014;
				maxRev = 70489;
				return true;

			case ConfuserVersion.v16_r71742:
				minRev = 71742;
				maxRev = 72868;
				return true;

			case ConfuserVersion.v17_r72989:
				minRev = 72989;
				maxRev = 72989;
				return true;

			case ConfuserVersion.v17_r73605:
				minRev = 73605;
				maxRev = 75267;
				return true;

			case ConfuserVersion.v18_r75288:
				minRev = 75288;
				maxRev = 75720;
				return true;

			case ConfuserVersion.v19_r75725:
				minRev = 75725;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
