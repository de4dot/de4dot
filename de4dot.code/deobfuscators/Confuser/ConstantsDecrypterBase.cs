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
	abstract class ConstantsDecrypterBase : IVersionProvider {
		protected ModuleDefinition module;
		protected byte[] fileData;
		protected ISimpleDeobfuscator simpleDeobfuscator;
		protected MethodDefinition nativeMethod;
		MethodDefinitionAndDeclaringTypeDict<DecrypterInfo> methodToDecrypterInfo = new MethodDefinitionAndDeclaringTypeDict<DecrypterInfo>();
		FieldDefinitionAndDeclaringTypeDict<bool> fields = new FieldDefinitionAndDeclaringTypeDict<bool>();
		protected EmbeddedResource resource;
		protected BinaryReader reader;

		public class DecrypterInfo {
			public MethodDefinition decryptMethod;
			public uint key0, key1, key2, key3;
			public byte doubleType, singleType, int32Type, int64Type, stringType;

			public void initialize() {
				if (!initializeKeys())
					throw new ApplicationException("Could not find all keys");
				if (!initializeTypeCodes())
					throw new ApplicationException("Could not find all type codes");
			}

			protected virtual bool initializeKeys() {
				if (!findKey0(decryptMethod, out key0))
					return false;
				if (!findKey1(decryptMethod, out key1))
					return false;
				if (!findKey2Key3(decryptMethod, out key2, out key3))
					return false;

				return true;
			}

			protected static bool findKey0(MethodDefinition method, out uint key) {
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

			protected static bool findKey2Key3(MethodDefinition method, out uint key2, out uint key3) {
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

			public uint calcHash(uint x) {
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

		public abstract bool Detected { get; }

		public MethodDefinition NativeMethod {
			get { return nativeMethod; }
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public IEnumerable<FieldDefinition> Fields {
			get { return fields.getKeys(); }
		}

		protected bool HasDecrypterInfos {
			get { return methodToDecrypterInfo.Count > 0; }
		}

		public IEnumerable<DecrypterInfo> DecrypterInfos {
			get { return methodToDecrypterInfo.getValues(); }
		}

		public ConstantsDecrypterBase(ModuleDefinition module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.fileData = fileData;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public abstract bool getRevisionRange(out int minRev, out int maxRev);
		public abstract void initialize();

		protected void add(DecrypterInfo info) {
			methodToDecrypterInfo.add(info.decryptMethod, info);
		}

		protected bool add(FieldDefinition field) {
			if (field == null)
				return false;
			fields.add(field, true);
			return true;
		}

		protected void initializeDecrypterInfos() {
			foreach (var info in methodToDecrypterInfo.getValues()) {
				simpleDeobfuscator.deobfuscate(info.decryptMethod);
				info.initialize();
			}
		}

		protected void setConstantsData(byte[] constants) {
			reader = new BinaryReader(new MemoryStream(constants));
		}

		protected EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		protected static MethodDefinition findNativeMethod(MethodDefinition method) {
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
				int index = i;
				var stloc = instrs[index++];
				if (!DotNetUtils.isStloc(stloc) || DotNetUtils.getLocalVar(method.Body.Variables, stloc) != local)
					continue;
				if (!DotNetUtils.isLdloc(instrs[index++]))
					continue;
				if (instrs[index].OpCode.Code == Code.Call) {
					if (i + 7 >= instrs.Count)
						continue;
					index++;
					if (!DotNetUtils.isLdloc(instrs[index++]))
						continue;
				}
				if (!DotNetUtils.isLdloc(instrs[index++]))
					continue;
				var ldloc = instrs[index++];
				if (!DotNetUtils.isLdloc(ldloc) || DotNetUtils.getLocalVar(method.Body.Variables, ldloc) != local)
					continue;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					continue;
				if (instrs[index++].OpCode.Code != Code.Stelem_I1)
					continue;

				return i;
			}
			return -1;
		}

		static int getDynamicEndIndex_v17_r74788(MethodDefinition method, VariableDefinition local) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 11; i++) {
				var stloc = instrs[i];
				if (!DotNetUtils.isStloc(stloc) || DotNetUtils.getLocalVar(method.Body.Variables, stloc) != local)
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 1]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 2]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 3]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 4]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 5]))
					continue;
				var ldci4 = instrs[i + 6];
				if (!DotNetUtils.isLdcI4(ldci4) || (DotNetUtils.getLdcI4Value(ldci4) != 8 && DotNetUtils.getLdcI4Value(ldci4) != 16))
					continue;
				if (instrs[i + 7].OpCode.Code != Code.Rem)
					continue;
				if (instrs[i + 8].OpCode.Code != Code.Ldelem_U1)
					continue;
				if (instrs[i + 9].OpCode.Code != Code.Xor)
					continue;
				if (instrs[i + 10].OpCode.Code != Code.Conv_U1)
					continue;
				if (instrs[i + 11].OpCode.Code != Code.Stelem_I1)
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

		static readonly byte[] defaultDecryptKey_v17 = new byte[1];
		protected byte[] decryptConstant_v17_r73740_dynamic(DecrypterInfo info, byte[] encrypted, uint offs, uint key) {
			return decryptConstant_v17_r73740_dynamic(info, encrypted, offs, key, defaultDecryptKey_v17);
		}

		protected byte[] decryptConstant_v17_r73740_dynamic(DecrypterInfo info, byte[] encrypted, uint offs, uint key1, byte[] key2) {
			var local = getDynamicLocal_v17_r73740(info.decryptMethod);
			if (local == null)
				throw new ApplicationException("Could not find local");

			int endIndex = getDynamicEndIndex_v17_r73740(info.decryptMethod, local);
			if (endIndex < 0)
				endIndex = getDynamicEndIndex_v17_r74788(info.decryptMethod, local);
			int startIndex = getDynamicStartIndex_v17_r73740(info.decryptMethod, endIndex);
			if (startIndex < 0)
				throw new ApplicationException("Could not find start/end index");

			var constReader = new ConstantsReader(info.decryptMethod);
			return decrypt(encrypted, key1, (magic, i) => {
				constReader.setConstantInt32(local, magic);
				int index = startIndex, result;
				if (!constReader.getNextInt32(ref index, out result) || index != endIndex)
					throw new ApplicationException("Could not decrypt integer");
				return (byte)(result ^ key2[i % key2.Length]);
			});
		}

		protected byte[] decryptConstant_v17_r73764_native(DecrypterInfo info, byte[] encrypted, uint offs, uint key) {
			return decryptConstant_v17_r73764_native(info, encrypted, offs, key, defaultDecryptKey_v17);
		}

		protected byte[] decryptConstant_v17_r73764_native(DecrypterInfo info, byte[] encrypted, uint offs, uint key1, byte[] key2) {
			var x86Emu = new x86Emulator(new PeImage(fileData));
			return decrypt(encrypted, key1, (magic, i) => (byte)(x86Emu.emulate((uint)nativeMethod.RVA, magic) ^ key2[i % key2.Length]));
		}

		static byte[] decrypt(byte[] encrypted, uint key, Func<uint, int, byte> decryptFunc) {
			var reader = new BinaryReader(new MemoryStream(encrypted));
			var decrypted = new byte[reader.ReadInt32() ^ key];
			for (int i = 0; i < decrypted.Length; i++) {
				uint magic = Utils.readEncodedUInt32(reader);
				decrypted[i] = decryptFunc(magic, i);
			}

			return decrypted;
		}

		public object decryptInt32(MethodDefinition caller, MethodDefinition decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.find(decryptMethod);
			byte typeCode;
			var data = decryptData(info, caller, args, out typeCode);
			if (typeCode != info.int32Type)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt32(data, 0);
		}

		public object decryptInt64(MethodDefinition caller, MethodDefinition decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.find(decryptMethod);
			byte typeCode;
			var data = decryptData(info, caller, args, out typeCode);
			if (typeCode != info.int64Type)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt64(data, 0);
		}

		public object decryptSingle(MethodDefinition caller, MethodDefinition decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.find(decryptMethod);
			byte typeCode;
			var data = decryptData(info, caller, args, out typeCode);
			if (typeCode != info.singleType)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToSingle(data, 0);
		}

		public object decryptDouble(MethodDefinition caller, MethodDefinition decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.find(decryptMethod);
			byte typeCode;
			var data = decryptData(info, caller, args, out typeCode);
			if (typeCode != info.doubleType)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToDouble(data, 0);
		}

		public string decryptString(MethodDefinition caller, MethodDefinition decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.find(decryptMethod);
			byte typeCode;
			var data = decryptData(info, caller, args, out typeCode);
			if (typeCode != info.stringType)
				return null;
			return Encoding.UTF8.GetString(data);
		}

		protected abstract byte[] decryptData(DecrypterInfo info, MethodDefinition caller, object[] args, out byte typeCode);
	}
}
