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
using System.IO;
using SevenZip.Compression.LZMA;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	static class ConfuserUtils {
		public static int FindCallMethod(IList<Instruction> instrs, int index, Code callCode, string methodFullName) {
			for (int i = index; i < instrs.Count; i++) {
				if (!IsCallMethod(instrs[i], callCode, methodFullName))
					continue;

				return i;
			}
			return -1;
		}

		public static int FindCallMethod(IList<Instr> instrs, int index, Code callCode, string methodFullName) {
			for (int i = index; i < instrs.Count; i++) {
				if (!IsCallMethod(instrs[i].Instruction, callCode, methodFullName))
					continue;

				return i;
			}
			return -1;
		}

		public static bool IsCallMethod(Instruction instr, Code callCode, string methodFullName) {
			if (instr.OpCode.Code != callCode)
				return false;
			var calledMethod = instr.Operand as IMethod;
			return calledMethod != null && calledMethod.FullName == methodFullName;
		}

		public static bool RemoveResourceHookCode(Blocks blocks, MethodDef handler) {
			return RemoveResolveHandlerCode(blocks, handler, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)");
		}

		public static bool RemoveAssemblyHookCode(Blocks blocks, MethodDef handler) {
			return RemoveResolveHandlerCode(blocks, handler, "System.Void System.AppDomain::add_AssemblyResolve(System.ResolveEventHandler)");
		}

		static bool RemoveResolveHandlerCode(Blocks blocks, MethodDef handler, string installHandlerMethod) {
			bool modified = false;
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 4; i++) {
					var call = instrs[i];
					if (call.OpCode.Code != Code.Call)
						continue;
					var calledMethod = call.Operand as IMethod;
					if (calledMethod == null || calledMethod.FullName != "System.AppDomain System.AppDomain::get_CurrentDomain()")
						continue;

					if (instrs[i + 1].OpCode.Code != Code.Ldnull)
						continue;

					var ldftn = instrs[i + 2];
					if (ldftn.OpCode.Code != Code.Ldftn)
						continue;
					if (ldftn.Operand != handler)
						continue;

					var newobj = instrs[i + 3];
					if (newobj.OpCode.Code != Code.Newobj)
						continue;
					var ctor = newobj.Operand as IMethod;
					if (ctor == null || ctor.FullName != "System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)")
						continue;

					var callvirt = instrs[i + 4];
					if (callvirt.OpCode.Code != Code.Callvirt)
						continue;
					calledMethod = callvirt.Operand as IMethod;
					if (calledMethod == null || calledMethod.FullName != installHandlerMethod)
						continue;

					block.Remove(i, 5);
					modified = true;
				}
			}
			return modified;
		}

		public static byte[] DecryptCompressedInt32Data(Arg64ConstantsReader constReader, int exprStart, int exprEnd, IBinaryReader reader, byte[] decrypted) {
			for (int i = 0; i < decrypted.Length; i++) {
				constReader.Arg = reader.Read7BitEncodedInt32();
				int index = exprStart;
				long result;
				if (!constReader.GetInt64(ref index, out result) || index != exprEnd)
					throw new ApplicationException("Could not decrypt integer");
				decrypted[i] = (byte)result;
			}
			return decrypted;
		}

		static readonly byte[] defaultDecryptKey = new byte[1];
		public static byte[] Decrypt(uint seed, byte[] encrypted) {
			return Decrypt(seed, encrypted, defaultDecryptKey);
		}

		public static byte[] Decrypt(uint seed, byte[] encrypted, byte[] key) {
			var decrypted = new byte[encrypted.Length];
			ushort _m = (ushort)(seed >> 16);
			ushort _c = (ushort)seed;
			ushort m = _c; ushort c = _m;
			for (int i = 0; i < decrypted.Length; i++) {
				decrypted[i] = (byte)(encrypted[i] ^ (seed * m + c) ^ key[i % key.Length]);
				m = (ushort)(seed * m + _m);
				c = (ushort)(seed * c + _c);
			}
			return decrypted;
		}

		public static int CountCalls(MethodDef method, string methodFullName) {
			if (method == null || method.Body == null)
				return 0;
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt && instr.OpCode.Code != Code.Newobj)
					continue;
				var calledMethod = instr.Operand as IMethod;
				if (calledMethod != null && calledMethod.FullName == methodFullName)
					count++;
			}
			return count;
		}

		public static int CountCalls(MethodDef method, MethodDef calledMethod) {
			if (method == null || method.Body == null)
				return 0;
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt && instr.OpCode.Code != Code.Newobj)
					continue;
				if (instr.Operand == calledMethod)
					count++;
			}
			return count;
		}

		public static int CountOpCode(MethodDef method, Code code) {
			if (method == null || method.Body == null)
				return 0;

			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == code)
					count++;
			}
			return count;
		}

		public static byte[] SevenZipDecompress(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(data));
			var props = reader.ReadBytes(5);
			var decoder = new Decoder();
			decoder.SetDecoderProperties(props);
			long totalSize = reader.ReadInt64();
			long compressedSize = data.Length - props.Length - 8;
			var decompressed = new byte[totalSize];
			decoder.Code(reader.BaseStream, new MemoryStream(decompressed, true), compressedSize, totalSize, null);
			return decompressed;
		}

		// Finds the Lzma type by finding an instruction that allocates a new Lzma.Decoder
		public static TypeDef FindLzmaType(MethodDef method) {
			if (method == null || method.Body == null)
				return null;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Newobj)
					continue;
				var ctor = instr.Operand as MethodDef;
				if (ctor == null)
					continue;
				var ctorType = ctor.DeclaringType;
				if (ctorType == null)
					continue;
				if (!IsLzmaType(ctorType.DeclaringType))
					continue;

				return ctorType.DeclaringType;
			}

			return null;
		}

		static bool IsLzmaType(TypeDef type) {
			if (type == null)
				return false;

			if (type.NestedTypes.Count != 6)
				return false;
			if (!CheckLzmaMethods(type))
				return false;
			if (FindLzmaOutWindowType(type.NestedTypes) == null)
				return false;

			return true;
		}

		static bool CheckLzmaMethods(TypeDef type) {
			int methods = 0;
			foreach (var m in type.Methods) {
				if (m.IsStaticConstructor)
					continue;
				if (m.IsInstanceConstructor) {
					if (m.MethodSig.GetParamCount() != 0)
						return false;
					continue;
				}
				if (!DotNetUtils.IsMethod(m, "System.UInt32", "(System.UInt32)"))
					return false;
				methods++;
			}
			return methods == 1;
		}

		static readonly string[] outWindowFields = new string[] {
			"System.Byte[]",
			"System.UInt32",
			"System.IO.Stream",
		};
		static TypeDef FindLzmaOutWindowType(IEnumerable<TypeDef> types) {
			foreach (var type in types) {
				if (new FieldTypes(type).Exactly(outWindowFields))
					return type;
			}
			return null;
		}
	}
}
