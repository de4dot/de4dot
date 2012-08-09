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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class ConstantsDecrypterV15 : ConstantsDecrypterBase {
		ConfuserVersion version = ConfuserVersion.Unknown;
		DecrypterInfo theDecrypterInfo;

		enum ConfuserVersion {
			Unknown,
			v15_r60785_normal,
			v15_r60785_dynamic,
			v17_r73404_normal,
			v17_r73740_dynamic,
			v17_r73764_dynamic,
			v17_r73764_native,
			v17_r73822_normal,
			v17_r73822_dynamic,
			v17_r73822_native,
		}

		public override bool Detected {
			get { return theDecrypterInfo != null; }
		}

		public ConstantsDecrypterV15(ModuleDefinition module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator)
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
		public void find() {
			var type = DotNetUtils.getModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Object", "(System.UInt32)"))
					continue;

				DecrypterInfo info = new DecrypterInfo();
				var localTypes = new LocalTypes(method);
				if (localTypes.all(requiredLocals1)) {
					if (localTypes.exists("System.Collections.BitArray"))	// or System.Random
						version = ConfuserVersion.v15_r60785_normal;
					else if (DeobUtils.hasInteger(method, 0x100) &&
							DeobUtils.hasInteger(method, 0x10000) &&
							DeobUtils.hasInteger(method, 0xFFFF))
						version = ConfuserVersion.v17_r73404_normal;
					else if (DotNetUtils.callsMethod(method, "System.String System.Text.Encoding::GetString(System.Byte[])")) {
						if (findInstruction(method.Body.Instructions, 0, Code.Conv_I8) >= 0)
							version = ConfuserVersion.v15_r60785_dynamic;
						else
							version = ConfuserVersion.v17_r73740_dynamic;
					}
					else if (DotNetUtils.callsMethod(method, "System.String System.Text.Encoding::GetString(System.Byte[],System.Int32,System.Int32)")) {
						if ((nativeMethod = findNativeMethod(method)) == null)
							version = ConfuserVersion.v17_r73764_dynamic;
						else
							version = ConfuserVersion.v17_r73764_native;
					}
					else
						continue;
				}
				else if (localTypes.all(requiredLocals2)) {
					if (DeobUtils.hasInteger(method, 0x100) &&
						DeobUtils.hasInteger(method, 0x10000) &&
						DeobUtils.hasInteger(method, 0xFFFF))
						version = ConfuserVersion.v17_r73822_normal;
					else if ((nativeMethod = findNativeMethod(method)) == null)
						version = ConfuserVersion.v17_r73822_dynamic;
					else
						version = ConfuserVersion.v17_r73822_native;
				}
				else
					continue;

				info.decryptMethod = method;
				theDecrypterInfo = info;
				add(info);
				break;
			}
		}

		public override void initialize() {
			if ((resource = findResource(theDecrypterInfo.decryptMethod)) == null)
				throw new ApplicationException("Could not find encrypted consts resource");

			initializeDecrypterInfos();
			if (!initializeFields(theDecrypterInfo))
				throw new ApplicationException("Could not find all fields");

			setConstantsData(DeobUtils.inflate(resource.GetResourceData(), true));
		}

		bool initializeFields(DecrypterInfo info) {
			switch (version) {
			case ConfuserVersion.v17_r73822_normal:
			case ConfuserVersion.v17_r73822_dynamic:
			case ConfuserVersion.v17_r73822_native:
				if (!add(ConstantsDecrypterUtils.findDictField(info.decryptMethod, info.decryptMethod.DeclaringType)))
					return false;
				if (!add(ConstantsDecrypterUtils.findMemoryStreamField(info.decryptMethod, info.decryptMethod.DeclaringType)))
					return false;
				break;

			default:
				break;
			}

			return true;
		}

		protected override byte[] decryptData(DecrypterInfo info, MethodDefinition caller, object[] args, out byte typeCode) {
			uint offs = info.calcHash(caller.MetadataToken.ToUInt32()) ^ (uint)args[0];
			reader.BaseStream.Position = offs;
			typeCode = reader.ReadByte();
			if (typeCode != info.int32Type && typeCode != info.int64Type &&
				typeCode != info.singleType && typeCode != info.doubleType &&
				typeCode != info.stringType)
				throw new ApplicationException("Invalid type code");

			var encrypted = reader.ReadBytes(reader.ReadInt32());
			return decryptConstant(info, encrypted, offs);
		}

		byte[] decryptConstant(DecrypterInfo info, byte[] encrypted, uint offs) {
			switch (version) {
			case ConfuserVersion.v15_r60785_normal: return decryptConstant_v15_r60785_normal(info, encrypted, offs);
			case ConfuserVersion.v15_r60785_dynamic: return decryptConstant_v15_r60785_dynamic(info, encrypted, offs);
			case ConfuserVersion.v17_r73404_normal: return decryptConstant_v17_r73404_normal(info, encrypted, offs);
			case ConfuserVersion.v17_r73740_dynamic: return decryptConstant_v17_r73740_dynamic(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73764_dynamic: return decryptConstant_v17_r73740_dynamic(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73764_native: return decryptConstant_v17_r73764_native(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73822_normal: return decryptConstant_v17_r73404_normal(info, encrypted, offs);
			case ConfuserVersion.v17_r73822_dynamic: return decryptConstant_v17_r73740_dynamic(info, encrypted, offs, 0);
			case ConfuserVersion.v17_r73822_native: return decryptConstant_v17_r73764_native(info, encrypted, offs, 0);
			default: throw new ApplicationException("Invalid version");
			}
		}

		byte[] decryptConstant_v15_r60785_normal(DecrypterInfo info, byte[] encrypted, uint offs) {
			var rand = new Random((int)(info.key0 ^ offs));
			var decrypted = new byte[encrypted.Length];
			rand.NextBytes(decrypted);
			for (int i = 0; i < decrypted.Length; i++)
				decrypted[i] ^= encrypted[i];
			return decrypted;
		}

		byte[] decryptConstant_v15_r60785_dynamic(DecrypterInfo info, byte[] encrypted, uint offs) {
			var instrs = info.decryptMethod.Body.Instructions;
			int startIndex = getDynamicStartIndex_v15_r60785(instrs);
			int endIndex = getDynamicEndIndex_v15_r60785(instrs, startIndex);
			if (endIndex < 0)
				throw new ApplicationException("Could not find start/endIndex");

			var dataReader = new BinaryReader(new MemoryStream(encrypted));
			var decrypted = new byte[dataReader.ReadInt32()];
			var constReader = new Arg64ConstantsReader(instrs, false);
			ConfuserUtils.decryptCompressedInt32Data(constReader, startIndex, endIndex, dataReader, decrypted);
			return decrypted;
		}

		static int getDynamicStartIndex_v15_r60785(IList<Instruction> instrs) {
			int index = findInstruction(instrs, 0, Code.Conv_I8);
			if (index < 0)
				return -1;
			if (findInstruction(instrs, index + 1, Code.Conv_I8) >= 0)
				return -1;
			return index;
		}

		static int getDynamicEndIndex_v15_r60785(IList<Instruction> instrs, int index) {
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

		static int findInstruction(IList<Instruction> instrs, int index, Code code) {
			for (int i = index; i < instrs.Count; i++) {
				if (instrs[i].OpCode.Code == code)
					return i;
			}
			return -1;
		}

		byte[] decryptConstant_v17_r73404_normal(DecrypterInfo info, byte[] encrypted, uint offs) {
			return ConfuserUtils.decrypt(info.key0 ^ offs, encrypted);
		}

		public override bool getRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v15_r60785_normal:
			case ConfuserVersion.v15_r60785_dynamic:
				minRev = 60785;
				maxRev = 72989;
				return true;

			case ConfuserVersion.v17_r73404_normal:
				minRev = 73404;
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

			case ConfuserVersion.v17_r73822_normal:
			case ConfuserVersion.v17_r73822_dynamic:
			case ConfuserVersion.v17_r73822_native:
				minRev = 73822;
				maxRev = 74637;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
