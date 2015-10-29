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
using System.Collections.Generic;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	// From v1.5 r60785 to v1.7 r74637
	class ConstantsDecrypterV15 : ConstantsDecrypterBase {
		ConfuserVersion version = ConfuserVersion.Unknown;
		DecrypterInfo theDecrypterInfo;

		enum ConfuserVersion {
			Unknown,
			v15_r60785_normal,
			v15_r60785_dynamic,
			v17_r72989_dynamic,
			v17_r73404_normal,
			v17_r73740_dynamic,
			v17_r73764_dynamic,
			v17_r73764_native,
			v17_r73822_normal,
			v17_r73822_dynamic,
			v17_r73822_native,
			v17_r74021_dynamic,
			v17_r74021_native,
			// v1.7 r74637 was the last version using this constants encrypter.
		}

		public override bool Detected {
			get { return theDecrypterInfo != null; }
		}

		public ConstantsDecrypterV15(ModuleDefMD module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module, fileData, simpleDeobfuscator) {
		}

		static readonly string[] requiredLocals1 = new string[] {
			"System.Byte[]",
			"System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>",
			"System.IO.BinaryReader",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.Reflection.Assembly",
		};
		static readonly string[] requiredLocals2 = new string[] {
			"System.Byte[]",
			"System.IO.BinaryReader",
			"System.IO.Compression.DeflateStream",
			"System.Reflection.Assembly",
		};
		public void Find() {
			var type = DotNetUtils.GetModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Object", "(System.UInt32)"))
					continue;

				DecrypterInfo info = new DecrypterInfo();
				var localTypes = new LocalTypes(method);
				if (localTypes.All(requiredLocals1)) {
					if (localTypes.Exists("System.Collections.BitArray"))	// or System.Random
						version = ConfuserVersion.v15_r60785_normal;
					else if (DeobUtils.HasInteger(method, 0x100) &&
							DeobUtils.HasInteger(method, 0x10000) &&
							DeobUtils.HasInteger(method, 0xFFFF))
						version = ConfuserVersion.v17_r73404_normal;
					else if (DotNetUtils.CallsMethod(method, "System.String System.Text.Encoding::GetString(System.Byte[])")) {
						if (FindInstruction(method.Body.Instructions, 0, Code.Conv_I8) >= 0) {
							if (DotNetUtils.CallsMethod(method, "System.Void System.Console::WriteLine()"))
								version = ConfuserVersion.v15_r60785_dynamic;
							else
								version = ConfuserVersion.v17_r72989_dynamic;
						}
						else
							version = ConfuserVersion.v17_r73740_dynamic;
					}
					else if (DotNetUtils.CallsMethod(method, "System.String System.Text.Encoding::GetString(System.Byte[],System.Int32,System.Int32)")) {
						if ((nativeMethod = FindNativeMethod(method)) == null)
							version = ConfuserVersion.v17_r73764_dynamic;
						else
							version = ConfuserVersion.v17_r73764_native;
					}
					else
						continue;
				}
				else if (localTypes.All(requiredLocals2)) {
					if (DeobUtils.HasInteger(method, 0x100) &&
						DeobUtils.HasInteger(method, 0x10000) &&
						DeobUtils.HasInteger(method, 0xFFFF))
						version = ConfuserVersion.v17_r73822_normal;
					else if (DotNetUtils.CallsMethod(method, "System.Int32 System.Object::GetHashCode()")) {
						if ((nativeMethod = FindNativeMethod(method)) == null)
							version = ConfuserVersion.v17_r74021_dynamic;
						else
							version = ConfuserVersion.v17_r74021_native;
					}
					else if ((nativeMethod = FindNativeMethod(method)) == null)
						version = ConfuserVersion.v17_r73822_dynamic;
					else
						version = ConfuserVersion.v17_r73822_native;
				}
				else
					continue;

				info.decryptMethod = method;
				theDecrypterInfo = info;
				Add(info);
				break;
			}
		}

		public override void Initialize() {
			if ((resource = FindResource(theDecrypterInfo.decryptMethod)) == null)
				throw new ApplicationException("Could not find encrypted consts resource");

			InitializeDecrypterInfos();
			if (!InitializeFields(theDecrypterInfo))
				throw new ApplicationException("Could not find all fields");

			SetConstantsData(DeobUtils.Inflate(resource.GetResourceData(), true));
		}

		bool InitializeFields(DecrypterInfo info) {
			switch (version) {
			case ConfuserVersion.v17_r73822_normal:
			case ConfuserVersion.v17_r73822_dynamic:
			case ConfuserVersion.v17_r73822_native:
			case ConfuserVersion.v17_r74021_dynamic:
			case ConfuserVersion.v17_r74021_native:
				if (!Add(ConstantsDecrypterUtils.FindDictField(info.decryptMethod, info.decryptMethod.DeclaringType)))
					return false;
				if (!Add(ConstantsDecrypterUtils.FindMemoryStreamField(info.decryptMethod, info.decryptMethod.DeclaringType)))
					return false;
				break;

			default:
				break;
			}

			return true;
		}

		protected override byte[] DecryptData(DecrypterInfo info, MethodDef caller, object[] args, out byte typeCode) {
			uint offs = info.CalcHash(caller.MDToken.ToUInt32()) ^ (uint)args[0];
			reader.Position = offs;
			typeCode = reader.ReadByte();
			if (typeCode != info.int32Type && typeCode != info.int64Type &&
				typeCode != info.singleType && typeCode != info.doubleType &&
				typeCode != info.stringType)
				throw new ApplicationException("Invalid type code");

			var encrypted = reader.ReadBytes(reader.ReadInt32());
			return DecryptConstant(info, encrypted, offs);
		}

		byte[] DecryptConstant(DecrypterInfo info, byte[] encrypted, uint offs) {
			switch (version) {
			case ConfuserVersion.v15_r60785_normal: return DecryptConstant_v15_r60785_normal(info, encrypted, offs);
			case ConfuserVersion.v15_r60785_dynamic: return DecryptConstant_v15_r60785_dynamic(info, encrypted, offs);
			case ConfuserVersion.v17_r72989_dynamic: return DecryptConstant_v15_r60785_dynamic(info, encrypted, offs);
			case ConfuserVersion.v17_r73404_normal: return DecryptConstant_v17_r73404_normal(info, encrypted, offs);
			case ConfuserVersion.v17_r73740_dynamic: return DecryptConstant_v17_r73740_dynamic(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73764_dynamic: return DecryptConstant_v17_r73740_dynamic(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73764_native: return DecryptConstant_v17_r73764_native(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73822_normal: return DecryptConstant_v17_r73404_normal(info, encrypted, offs);
			case ConfuserVersion.v17_r73822_dynamic: return DecryptConstant_v17_r73740_dynamic(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73822_native: return DecryptConstant_v17_r73764_native(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r74021_dynamic: return DecryptConstant_v17_r73740_dynamic(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r74021_native: return DecryptConstant_v17_r73764_native(info, encrypted, offs, 0);
			default: throw new ApplicationException("Invalid version");
			}
		}

		byte[] DecryptConstant_v15_r60785_normal(DecrypterInfo info, byte[] encrypted, uint offs) {
			var rand = new Random((int)(info.key0 ^ offs));
			var decrypted = new byte[encrypted.Length];
			rand.NextBytes(decrypted);
			for (int i = 0; i < decrypted.Length; i++)
				decrypted[i] ^= encrypted[i];
			return decrypted;
		}

		byte[] DecryptConstant_v15_r60785_dynamic(DecrypterInfo info, byte[] encrypted, uint offs) {
			var instrs = info.decryptMethod.Body.Instructions;
			int startIndex = GetDynamicStartIndex_v15_r60785(instrs);
			int endIndex = GetDynamicEndIndex_v15_r60785(instrs, startIndex);
			if (endIndex < 0)
				throw new ApplicationException("Could not find start/endIndex");

			var dataReader = MemoryImageStream.Create(encrypted);
			var decrypted = new byte[dataReader.ReadInt32()];
			var constReader = new Arg64ConstantsReader(instrs, false);
			ConfuserUtils.DecryptCompressedInt32Data(constReader, startIndex, endIndex, dataReader, decrypted);
			return decrypted;
		}

		static int GetDynamicStartIndex_v15_r60785(IList<Instruction> instrs) {
			int index = FindInstruction(instrs, 0, Code.Conv_I8);
			if (index < 0)
				return -1;
			if (FindInstruction(instrs, index + 1, Code.Conv_I8) >= 0)
				return -1;
			return index;
		}

		static int GetDynamicEndIndex_v15_r60785(IList<Instruction> instrs, int index) {
			if (index < 0)
				return -1;
			for (int i = index; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
				if (instr.OpCode.Code == Code.Conv_U1)
					return i;
			}
			return -1;
		}

		static int FindInstruction(IList<Instruction> instrs, int index, Code code) {
			for (int i = index; i < instrs.Count; i++) {
				if (instrs[i].OpCode.Code == code)
					return i;
			}
			return -1;
		}

		byte[] DecryptConstant_v17_r73404_normal(DecrypterInfo info, byte[] encrypted, uint offs) {
			return ConfuserUtils.Decrypt(info.key0 ^ offs, encrypted);
		}

		public override bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v15_r60785_normal:
				minRev = 60785;
				maxRev = 72989;
				return true;

			case ConfuserVersion.v17_r73404_normal:
				minRev = 73404;
				maxRev = 73791;
				return true;

			case ConfuserVersion.v17_r73822_normal:
				minRev = 73822;
				maxRev = 74637;
				return true;

			case ConfuserVersion.v15_r60785_dynamic:
				minRev = 60785;
				maxRev = 72868;
				return true;

			case ConfuserVersion.v17_r72989_dynamic:
				minRev = 72989;
				maxRev = 73605;
				return true;

			case ConfuserVersion.v17_r73740_dynamic:
				minRev = 73740;
				maxRev = 73740;
				return true;

			case ConfuserVersion.v17_r73764_dynamic:
			case ConfuserVersion.v17_r73764_native:
				minRev = 73764;
				maxRev = 73791;
				return true;

			case ConfuserVersion.v17_r73822_dynamic:
			case ConfuserVersion.v17_r73822_native:
				minRev = 73822;
				maxRev = 73822;
				return true;

			case ConfuserVersion.v17_r74021_dynamic:
			case ConfuserVersion.v17_r74021_native:
				minRev = 74021;
				maxRev = 74637;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
