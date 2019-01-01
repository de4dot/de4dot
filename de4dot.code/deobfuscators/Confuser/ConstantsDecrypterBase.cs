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
using System.Text;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	abstract class ConstantsDecrypterBase : IVersionProvider {
		protected ModuleDefMD module;
		protected byte[] fileData;
		protected ISimpleDeobfuscator simpleDeobfuscator;
		protected MethodDef nativeMethod;
		MethodDefAndDeclaringTypeDict<DecrypterInfo> methodToDecrypterInfo = new MethodDefAndDeclaringTypeDict<DecrypterInfo>();
		FieldDefAndDeclaringTypeDict<bool> fields = new FieldDefAndDeclaringTypeDict<bool>();
		protected EmbeddedResource resource;
		protected DataReader reader;

		public class DecrypterInfo {
			public MethodDef decryptMethod;
			public uint key0, key1, key2, key3;
			public byte doubleType, singleType, int32Type, int64Type, stringType;

			public void Initialize() {
				if (!InitializeKeys())
					throw new ApplicationException("Could not find all keys");
				if (!InitializeTypeCodes())
					throw new ApplicationException("Could not find all type codes");
			}

			protected virtual bool InitializeKeys() {
				if (!FindKey0(decryptMethod, out key0))
					return false;
				if (!FindKey1(decryptMethod, out key1))
					return false;
				if (!FindKey2Key3(decryptMethod, out key2, out key3))
					return false;

				return true;
			}

			protected static bool FindKey0(MethodDef method, out uint key) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 5; i++) {
					if (!instrs[i].IsLdloc())
						continue;
					if (instrs[i + 1].OpCode.Code != Code.Or)
						continue;
					var ldci4 = instrs[i + 2];
					if (!ldci4.IsLdcI4())
						continue;
					if (instrs[i + 3].OpCode.Code != Code.Xor)
						continue;
					if (instrs[i + 4].OpCode.Code != Code.Add)
						continue;
					if (!instrs[i + 5].IsStloc())
						continue;

					key = (uint)ldci4.GetLdcI4Value();
					return true;
				}
				key = 0;
				return false;
			}

			static bool FindKey1(MethodDef method, out uint key) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()");
					if (index < 0)
						break;
					if (index + 2 > instrs.Count)
						break;
					if (!instrs[index + 1].IsStloc())
						continue;
					var ldci4 = instrs[index + 2];
					if (!ldci4.IsLdcI4())
						continue;

					key = (uint)ldci4.GetLdcI4Value();
					return true;
				}
				key = 0;
				return false;
			}

			protected static bool FindKey2Key3(MethodDef method, out uint key2, out uint key3) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 3; i++) {
					var ldci4_1 = instrs[i];
					if (!ldci4_1.IsLdcI4())
						continue;
					if (!instrs[i + 1].IsStloc())
						continue;
					var ldci4_2 = instrs[i + 2];
					if (!ldci4_2.IsLdcI4())
						continue;
					if (!instrs[i + 3].IsStloc())
						continue;

					key2 = (uint)ldci4_1.GetLdcI4Value();
					key3 = (uint)ldci4_2.GetLdcI4Value();
					return true;
				}
				key2 = 0;
				key3 = 0;
				return false;
			}

			bool InitializeTypeCodes() {
				var allBlocks = new Blocks(decryptMethod).MethodBlocks.GetAllBlocks();
				if (!FindTypeCode(allBlocks, out doubleType, Code.Call, "System.Double System.BitConverter::ToDouble(System.Byte[],System.Int32)"))
					return false;
				if (!FindTypeCode(allBlocks, out singleType, Code.Call, "System.Single System.BitConverter::ToSingle(System.Byte[],System.Int32)"))
					return false;
				if (!FindTypeCode(allBlocks, out int32Type, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)"))
					return false;
				if (!FindTypeCode(allBlocks, out int64Type, Code.Call, "System.Int64 System.BitConverter::ToInt64(System.Byte[],System.Int32)"))
					return false;
				if (!FindTypeCode(allBlocks, out stringType, Code.Callvirt, "System.String System.Text.Encoding::GetString(System.Byte[])") &&
					!FindTypeCode(allBlocks, out stringType, Code.Callvirt, "System.String System.Text.Encoding::GetString(System.Byte[],System.Int32,System.Int32)"))
					return false;
				return true;
			}

			static bool FindTypeCode(IList<Block> allBlocks, out byte typeCode, Code callCode, string bitConverterMethod) {
				foreach (var block in allBlocks) {
					if (block.Sources.Count != 1)
						continue;
					int index = ConfuserUtils.FindCallMethod(block.Instructions, 0, callCode, bitConverterMethod);
					if (index < 0)
						continue;

					if (!FindTypeCode(block.Sources[0], out typeCode))
						continue;

					return true;
				}
				typeCode = 0;
				return false;
			}

			static Block FixBlock(Block block) {
				if (block.Sources.Count != 1)
					return block;
				if (block.GetOnlyTarget() == null)
					return block;
				if (block.Instructions.Count == 0) {
				}
				else if (block.Instructions.Count == 1 && block.Instructions[0].OpCode.Code == Code.Nop) {
				}
				else
					return block;
				return block.Sources[0];
			}

			static bool FindTypeCode(Block block, out byte typeCode) {
				block = FixBlock(block);

				var instrs = block.Instructions;
				int numCeq = 0;
				for (int i = instrs.Count - 1; i >= 0; i--) {
					var instr = instrs[i];
					if (instr.OpCode.Code == Code.Ceq) {
						numCeq++;
						continue;
					}
					if (!instr.Instruction.IsLdcI4())
						continue;
					if (numCeq != 0 && numCeq != 2)
						continue;

					typeCode = (byte)instr.Instruction.GetLdcI4Value();
					return true;
				}
				typeCode = 0;
				return false;
			}

			public uint CalcHash(uint x) {
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

		public MethodDef NativeMethod => nativeMethod;
		public EmbeddedResource Resource => resource;
		public IEnumerable<FieldDef> Fields => fields.GetKeys();
		protected bool HasDecrypterInfos => methodToDecrypterInfo.Count > 0;
		public IEnumerable<DecrypterInfo> DecrypterInfos => methodToDecrypterInfo.GetValues();

		public ConstantsDecrypterBase(ModuleDefMD module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.fileData = fileData;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public abstract bool GetRevisionRange(out int minRev, out int maxRev);
		public abstract void Initialize();

		protected void Add(DecrypterInfo info) => methodToDecrypterInfo.Add(info.decryptMethod, info);

		protected bool Add(FieldDef field) {
			if (field == null)
				return false;
			fields.Add(field, true);
			return true;
		}

		protected void InitializeDecrypterInfos() {
			foreach (var info in methodToDecrypterInfo.GetValues()) {
				simpleDeobfuscator.Deobfuscate(info.decryptMethod);
				info.Initialize();
			}
		}

		protected void SetConstantsData(byte[] constants) => reader = ByteArrayDataReaderFactory.CreateReader(constants);
		protected EmbeddedResource FindResource(MethodDef method) => DotNetUtils.GetResource(module, DotNetUtils.GetCodeStrings(method)) as EmbeddedResource;

		protected static MethodDef FindNativeMethod(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var call = instrs[i];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || !calledMethod.IsNative)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Int32", "(System.Int32)"))
					continue;

				return calledMethod;
			}
			return null;
		}

		static Local GetDynamicLocal_v17_r73740(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Byte System.IO.BinaryReader::ReadByte()");
				if (i < 0 || i + 5 >= instrs.Count)
					break;
				if (!instrs[i + 1].IsStloc())
					continue;
				var ldloc = instrs[i + 2];
				if (!ldloc.IsLdloc())
					continue;
				if (!instrs[i + 3].IsLdloc())
					continue;
				var ldci4 = instrs[i + 4];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 0x7F)
					continue;
				if (instrs[i + 5].OpCode.Code != Code.And)
					continue;

				return ldloc.GetLocal(method.Body.Variables);
			}
			return null;
		}

		static int GetDynamicEndIndex_v17_r73740(MethodDef method, Local local) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 5; i++) {
				int index = i;
				var stloc = instrs[index++];
				if (!stloc.IsStloc() || stloc.GetLocal(method.Body.Variables) != local)
					continue;
				if (!instrs[index++].IsLdloc())
					continue;
				if (instrs[index].OpCode.Code == Code.Call) {
					if (i + 7 >= instrs.Count)
						continue;
					index++;
					if (!instrs[index++].IsLdloc())
						continue;
				}
				if (!instrs[index++].IsLdloc())
					continue;
				var ldloc = instrs[index++];
				if (!ldloc.IsLdloc() || ldloc.GetLocal(method.Body.Variables) != local)
					continue;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					continue;
				if (instrs[index++].OpCode.Code != Code.Stelem_I1)
					continue;

				return i;
			}
			return -1;
		}

		static int GetDynamicEndIndex_v17_r74788(MethodDef method, Local local) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 11; i++) {
				var stloc = instrs[i];
				if (!stloc.IsStloc() || stloc.GetLocal(method.Body.Variables) != local)
					continue;
				if (!instrs[i + 1].IsLdloc())
					continue;
				if (!instrs[i + 2].IsLdloc())
					continue;
				if (!instrs[i + 3].IsLdloc())
					continue;
				if (!instrs[i + 4].IsLdloc())
					continue;
				if (!instrs[i + 5].IsLdloc())
					continue;
				var ldci4 = instrs[i + 6];
				if (!ldci4.IsLdcI4() || (ldci4.GetLdcI4Value() != 8 && ldci4.GetLdcI4Value() != 16))
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

		static int GetDynamicStartIndex_v17_r73740(MethodDef method, int endIndex) {
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
		protected byte[] DecryptConstant_v17_r73740_dynamic(DecrypterInfo info, byte[] encrypted, uint offs, uint key) =>
			DecryptConstant_v17_r73740_dynamic(info, encrypted, offs, key, defaultDecryptKey_v17);

		protected byte[] DecryptConstant_v17_r73740_dynamic(DecrypterInfo info, byte[] encrypted, uint offs, uint key1, byte[] key2) {
			var local = GetDynamicLocal_v17_r73740(info.decryptMethod);
			if (local == null)
				throw new ApplicationException("Could not find local");

			int endIndex = GetDynamicEndIndex_v17_r73740(info.decryptMethod, local);
			if (endIndex < 0)
				endIndex = GetDynamicEndIndex_v17_r74788(info.decryptMethod, local);
			int startIndex = GetDynamicStartIndex_v17_r73740(info.decryptMethod, endIndex);
			if (startIndex < 0)
				throw new ApplicationException("Could not find start/end index");

			var constReader = new ConstantsReader(info.decryptMethod);
			return Decrypt(encrypted, key1, (magic, i) => {
				constReader.SetConstantInt32(local, magic);
				int index = startIndex;
				if (!constReader.GetNextInt32(ref index, out int result) || index != endIndex)
					throw new ApplicationException("Could not decrypt integer");
				return (byte)(result ^ key2[i % key2.Length]);
			});
		}

		protected byte[] DecryptConstant_v17_r73764_native(DecrypterInfo info, byte[] encrypted, uint offs, uint key) =>
			DecryptConstant_v17_r73764_native(info, encrypted, offs, key, defaultDecryptKey_v17);

		protected byte[] DecryptConstant_v17_r73764_native(DecrypterInfo info, byte[] encrypted, uint offs, uint key1, byte[] key2) {
			using (var x86Emu = new X86Emulator(fileData))
				return Decrypt(encrypted, key1, (magic, i) => (byte)(x86Emu.Emulate((uint)nativeMethod.RVA, magic) ^ key2[i % key2.Length]));
		}

		static byte[] Decrypt(byte[] encrypted, uint key, Func<uint, int, byte> decryptFunc) {
			var reader = ByteArrayDataReaderFactory.CreateReader(encrypted);
			var decrypted = new byte[reader.ReadInt32() ^ key];
			for (int i = 0; i < decrypted.Length; i++) {
				uint magic = reader.Read7BitEncodedUInt32();
				decrypted[i] = decryptFunc(magic, i);
			}

			return decrypted;
		}

		public object DecryptInt32(MethodDef caller, MethodDef decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.Find(decryptMethod);
			var data = DecryptData(info, caller, args, out byte typeCode);
			if (typeCode != info.int32Type)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt32(data, 0);
		}

		public object DecryptInt64(MethodDef caller, MethodDef decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.Find(decryptMethod);
			var data = DecryptData(info, caller, args, out byte typeCode);
			if (typeCode != info.int64Type)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToInt64(data, 0);
		}

		public object DecryptSingle(MethodDef caller, MethodDef decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.Find(decryptMethod);
			var data = DecryptData(info, caller, args, out byte typeCode);
			if (typeCode != info.singleType)
				return null;
			if (data.Length != 4)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToSingle(data, 0);
		}

		public object DecryptDouble(MethodDef caller, MethodDef decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.Find(decryptMethod);
			var data = DecryptData(info, caller, args, out byte typeCode);
			if (typeCode != info.doubleType)
				return null;
			if (data.Length != 8)
				throw new ApplicationException("Invalid data length");
			return BitConverter.ToDouble(data, 0);
		}

		public string DecryptString(MethodDef caller, MethodDef decryptMethod, object[] args) {
			var info = methodToDecrypterInfo.Find(decryptMethod);
			var data = DecryptData(info, caller, args, out byte typeCode);
			if (typeCode != info.stringType)
				return null;
			return Encoding.UTF8.GetString(data);
		}

		protected abstract byte[] DecryptData(DecrypterInfo info, MethodDef caller, object[] args, out byte typeCode);
	}
}
