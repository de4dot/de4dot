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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.Confuser {
	class ConstantsDecrypterV15 {
		ModuleDefinition module;
		byte[] fileData;
		ISimpleDeobfuscator simpleDeobfuscator;
		FieldDefinition dictField, streamField;
		MethodDefinition decryptMethod;
		MethodDefinition nativeMethod;
		EmbeddedResource resource;
		uint key0, key1, key2, key3;
		byte doubleType, singleType, int32Type, int64Type, stringType;
		BinaryReader reader;
		ConfuserVersion version = ConfuserVersion.Unknown;

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

		public MethodDefinition Method {
			get { return decryptMethod; }
		}

		public MethodDefinition NativeMethod {
			get { return nativeMethod; }
		}

		public IEnumerable<FieldDefinition> Fields {
			get {
				return new List<FieldDefinition> {
					streamField,
					dictField,
				};
			}
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public ConstantsDecrypterV15(ModuleDefinition module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.fileData = fileData;
			this.simpleDeobfuscator = simpleDeobfuscator;
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

				decryptMethod = method;
				break;
			}
		}

		static MethodDefinition findNativeMethod(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var call = instrs[i];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null || !calledMethod.IsStatic || !calledMethod.IsNative)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int32", "(System.Int32)"))
					continue;

				return calledMethod;
			}
			return null;
		}

		public void initialize() {
			if ((resource = findResource(decryptMethod)) == null)
				throw new ApplicationException("Could not find encrypted consts resource");

			simpleDeobfuscator.deobfuscate(decryptMethod);
			if (!initializeKeys())
				throw new ApplicationException("Could not find all keys");
			if (!initializeTypeCodes())
				throw new ApplicationException("Could not find all type codes");
			if (!initializeFields())
				throw new ApplicationException("Could not find all fields");

			var constants = DeobUtils.inflate(resource.GetResourceData(), true);
			reader = new BinaryReader(new MemoryStream(constants));
		}

		bool initializeFields() {
			switch (version) {
			case ConfuserVersion.v17_r73822_normal:
			case ConfuserVersion.v17_r73822_dynamic:
			case ConfuserVersion.v17_r73822_native:
				if ((dictField = findDictField(decryptMethod, decryptMethod.DeclaringType)) == null)
					return false;
				if ((streamField = findStreamField(decryptMethod, decryptMethod.DeclaringType)) == null)
					return false;
				break;

			default:
				break;
			}

			return true;
		}

		static FieldDefinition findDictField(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var newobj = instrs[i];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;
				var ctor = newobj.Operand as MethodReference;
				if (ctor == null || ctor.FullName != "System.Void System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>::.ctor()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>")
					continue;

				return field;
			}
			return null;
		}

		static FieldDefinition findStreamField(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var newobj = instrs[i];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;
				var ctor = newobj.Operand as MethodReference;
				if (ctor == null || ctor.FullName != "System.Void System.IO.MemoryStream::.ctor()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.IO.MemoryStream")
					continue;

				return field;
			}
			return null;
		}

		bool initializeKeys() {
			if (!findKey0(decryptMethod, out key0))
				return false;
			if (!findKey1(decryptMethod, out key1))
				return false;
			if (!findKey2Key3(decryptMethod, out key2, out key3))
				return false;

			return true;
		}

		static bool findKey0(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 5; i++) {
				if (!DotNetUtils.isLdloc(instrs[i]))
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Or)
					continue;
				var ldci4 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Xor)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Add)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 5]))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		static bool findKey1(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()");
				if (index < 0)
					break;
				if (index + 2 > instrs.Count)
					break;
				if (!DotNetUtils.isStloc(instrs[index + 1]))
					continue;
				var ldci4 = instrs[index + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		static bool findKey2Key3(MethodDefinition method, out uint key2, out uint key3) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 1]))
					continue;
				var ldci4_2 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 3]))
					continue;

				key2 = (uint)DotNetUtils.getLdcI4Value(ldci4_1);
				key3 = (uint)DotNetUtils.getLdcI4Value(ldci4_2);
				return true;
			}
			key2 = 0;
			key3 = 0;
			return false;
		}

		bool initializeTypeCodes() {
			var allBlocks = new Blocks(decryptMethod).MethodBlocks.getAllBlocks();
			if (!findTypeCode(allBlocks, out doubleType, Code.Call, "System.Double System.BitConverter::ToDouble(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out singleType, Code.Call, "System.Single System.BitConverter::ToSingle(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out int32Type, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out int64Type, Code.Call, "System.Int64 System.BitConverter::ToInt64(System.Byte[],System.Int32)"))
				return false;
			if (!findTypeCode(allBlocks, out stringType, Code.Callvirt, "System.String System.Text.Encoding::GetString(System.Byte[])") &&
				!findTypeCode(allBlocks, out stringType, Code.Callvirt, "System.String System.Text.Encoding::GetString(System.Byte[],System.Int32,System.Int32)"))
				return false;
			return true;
		}

		static bool findTypeCode(IList<Block> allBlocks, out byte typeCode, Code callCode, string bitConverterMethod) {
			foreach (var block in allBlocks) {
				if (block.Sources.Count != 1)
					continue;
				int index = ConfuserUtils.findCallMethod(block.Instructions, 0, callCode, bitConverterMethod);
				if (index < 0)
					continue;

				if (!findTypeCode(block.Sources[0], out typeCode))
					continue;

				return true;
			}
			typeCode = 0;
			return false;
		}

		static Block fixBlock(Block block) {
			if (block.Sources.Count != 1)
				return block;
			if (block.getOnlyTarget() == null)
				return block;
			if (block.Instructions.Count == 0) {
			}
			else if (block.Instructions.Count == 1 && block.Instructions[0].OpCode.Code == Code.Nop) {
			}
			else
				return block;
			return block.Sources[0];
		}

		static bool findTypeCode(Block block, out byte typeCode) {
			block = fixBlock(block);

			var instrs = block.Instructions;
			int numCeq = 0;
			for (int i = instrs.Count - 1; i >= 0; i--) {
				var instr = instrs[i];
				if (instr.OpCode.Code == Code.Ceq) {
					numCeq++;
					continue;
				}
				if (!DotNetUtils.isLdcI4(instr.Instruction))
					continue;
				if (numCeq != 0 && numCeq != 2)
					continue;

				typeCode = (byte)DotNetUtils.getLdcI4Value(instr.Instruction);
				return true;
			}
			typeCode = 0;
			return false;
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		public object decryptInt32(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != int32Type)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt32(data, 0);
		}

		public object decryptInt64(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != int64Type)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt64(data, 0);
		}

		public object decryptSingle(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != singleType)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToSingle(data, 0);
		}

		public object decryptDouble(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != doubleType)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToDouble(data, 0);
		}

		public string decryptString(MethodDefinition caller, uint magic) {
			byte typeCode;
			var data = decryptData(caller, magic, out typeCode);
			if (typeCode != stringType)
				return null;
			return Encoding.UTF8.GetString(data);
		}

		byte[] decryptData(MethodDefinition caller, uint magic, out byte typeCode) {
			uint offs = calcHash(caller.MetadataToken.ToUInt32()) ^ magic;
			reader.BaseStream.Position = offs;
			typeCode = reader.ReadByte();
			if (typeCode != int32Type && typeCode != int64Type &&
				typeCode != singleType && typeCode != doubleType &&
				typeCode != stringType)
				throw new ApplicationException("Invalid type code");

			var encrypted = reader.ReadBytes(reader.ReadInt32());
			return decryptConstant(encrypted, offs);
		}

		byte[] decryptConstant(byte[] encrypted, uint offs) {
			switch (version) {
			case ConfuserVersion.v15_r60785_normal: return decryptConstant_v15_r60785_normal(encrypted, offs);
			case ConfuserVersion.v15_r60785_dynamic: return decryptConstant_v15_r60785_dynamic(encrypted, offs);
			case ConfuserVersion.v17_r73404_normal: return decryptConstant_v17_r73404_normal(encrypted, offs);
			case ConfuserVersion.v17_r73740_dynamic: return decryptConstant_v17_r73740_dynamic(encrypted, offs);
			case ConfuserVersion.v17_r73764_dynamic: return decryptConstant_v17_r73740_dynamic(encrypted, offs);
			case ConfuserVersion.v17_r73764_native: return decryptConstant_v17_r73764_native(encrypted, offs);
			case ConfuserVersion.v17_r73822_normal: return decryptConstant_v17_r73404_normal(encrypted, offs);
			case ConfuserVersion.v17_r73822_dynamic: return decryptConstant_v17_r73740_dynamic(encrypted, offs);
			case ConfuserVersion.v17_r73822_native: return decryptConstant_v17_r73764_native(encrypted, offs);
			default: throw new ApplicationException("Invalid version");
			}
		}

		byte[] decryptConstant_v15_r60785_normal(byte[] encrypted, uint offs) {
			var rand = new Random((int)(key0 ^ offs));
			var decrypted = new byte[encrypted.Length];
			rand.NextBytes(decrypted);
			for (int i = 0; i < decrypted.Length; i++)
				decrypted[i] ^= encrypted[i];
			return decrypted;
		}

		byte[] decryptConstant_v15_r60785_dynamic(byte[] encrypted, uint offs) {
			var instrs = decryptMethod.Body.Instructions;
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

		byte[] decryptConstant_v17_r73404_normal(byte[] encrypted, uint offs) {
			return ConfuserUtils.decrypt(key0 ^ offs, encrypted);
		}

		static VariableDefinition getDynamicLocal_v17_r73740(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Byte System.IO.BinaryReader::ReadByte()");
				if (i < 0 || i + 5 >= instrs.Count)
					break;
				if (!DotNetUtils.isStloc(instrs[i + 1]))
					continue;
				var ldloc = instrs[i + 2];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 3]))
					continue;
				var ldci4 = instrs[i + 4];
				if (!DotNetUtils.isLdcI4(ldci4) || DotNetUtils.getLdcI4Value(ldci4) != 0x7F)
					continue;
				if (instrs[i + 5].OpCode.Code != Code.And)
					continue;

				return DotNetUtils.getLocalVar(method.Body.Variables, ldloc);
			}
			return null;
		}

		static int getDynamicEndIndex_v17_r73740(MethodDefinition method, VariableDefinition local) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 5; i++) {
				var stloc = instrs[i];
				if (!DotNetUtils.isStloc(stloc) || DotNetUtils.getLocalVar(method.Body.Variables, stloc) != local)
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 1]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 2]))
					continue;
				var ldloc = instrs[i + 3];
				if (!DotNetUtils.isLdloc(ldloc) || DotNetUtils.getLocalVar(method.Body.Variables, ldloc) != local)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Conv_U1)
					continue;
				if (instrs[i + 5].OpCode.Code != Code.Stelem_I1)
					continue;

				return i;
			}
			return -1;
		}

		static int getDynamicStartIndex_v17_r73740(MethodDefinition method, int endIndex) {
			if (endIndex < 0)
				return -1;
			var instrs = method.Body.Instructions;
			for (int i = endIndex; i >= 0; i--) {
				if (i == 0)
					return i == endIndex ? -1 : i + 1;
				if (instrs[i].OpCode.FlowControl == FlowControl.Next)
					continue;

				return i + 1;
			}
			return -1;
		}

		byte[] decryptConstant_v17_r73740_dynamic(byte[] encrypted, uint offs) {
			var local = getDynamicLocal_v17_r73740(decryptMethod);
			if (local == null)
				throw new ApplicationException("Could not find local");

			int endIndex = getDynamicEndIndex_v17_r73740(decryptMethod, local);
			int startIndex = getDynamicStartIndex_v17_r73740(decryptMethod, endIndex);
			if (startIndex < 0)
				throw new ApplicationException("Could not find start/end index");

			var constReader = new ConstantsReader(decryptMethod);
			return decrypt(encrypted, magic => {
				constReader.setConstantInt32(local, magic);
				int index = startIndex, result;
				if (!constReader.getNextInt32(ref index, out result) || index != endIndex)
					throw new ApplicationException("Could not decrypt integer");
				return (byte)result;
			});
		}

		byte[] decryptConstant_v17_r73764_native(byte[] encrypted, uint offs) {
			var x86Emu = new x86Emulator(new PeImage(fileData));
			return decrypt(encrypted, magic => (byte)x86Emu.emulate((uint)nativeMethod.RVA, magic));
		}

		byte[] decrypt(byte[] encrypted, Func<uint, byte> decryptFunc) {
			var reader = new BinaryReader(new MemoryStream(encrypted));
			var decrypted = new byte[reader.ReadInt32()];
			for (int i = 0; i < decrypted.Length; i++) {
				uint magic = Utils.readEncodedUInt32(reader);
				decrypted[i] = decryptFunc(magic);
			}

			return decrypted;
		}

		uint calcHash(uint x) {
			uint h0 = key1 ^ x;
			uint h1 = key2;
			uint h2 = key3;
			for (uint i = 1; i <= 64; i++) {
				h0 = (h0 << 8) | (h0 >> 24);
				uint n = h0 & 0x3F;
				if (n >= 0 && n < 16) {
					h1 |= ((byte)(h0 >> 8) & (h0 >> 16)) ^ (byte)~h0;
					h2 ^= (h0 * i + 1) & 0xF;
					h0 += (h1 | h2) ^ key0;
				}
				else if (n >= 16 && n < 32) {
					h1 ^= ((h0 & 0x00FF00FF) << 8) ^ (ushort)((h0 >> 8) | ~h0);
					h2 += (h0 * i) & 0x1F;
					h0 |= (h1 + ~h2) & key0;
				}
				else if (n >= 32 && n < 48) {
					h1 += (byte)(h0 | (h0 >> 16)) + (~h0 & 0xFF);
					h2 -= ~(h0 + n) % 48;
					h0 ^= (h1 % h2) | key0;
				}
				else if (n >= 48 && n < 64) {
					h1 ^= ((byte)(h0 >> 16) | ~(h0 & 0xFF)) * (~h0 & 0x00FF0000);
					h2 += (h0 ^ (i - 1)) % n;
					h0 -= ~(h1 ^ h2) + key0;
				}
			}
			return h0;
		}
	}
}
