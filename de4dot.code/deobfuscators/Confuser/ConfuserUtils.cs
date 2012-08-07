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
	static class ConfuserUtils {
		public static int findCallMethod(IList<Instruction> instrs, int index, Code callCode, string methodFullName) {
			for (int i = index; i < instrs.Count; i++) {
				if (!isCallMethod(instrs[i], callCode, methodFullName))
					continue;

				return i;
			}
			return -1;
		}

		public static int findCallMethod(IList<Instr> instrs, int index, Code callCode, string methodFullName) {
			for (int i = index; i < instrs.Count; i++) {
				if (!isCallMethod(instrs[i].Instruction, callCode, methodFullName))
					continue;

				return i;
			}
			return -1;
		}

		public static bool isCallMethod(Instruction instr, Code callCode, string methodFullName) {
			if (instr.OpCode.Code != callCode)
				return false;
			var calledMethod = instr.Operand as MethodReference;
			return calledMethod != null && calledMethod.FullName == methodFullName;
		}

		public static bool removeResourceHookCode(Blocks blocks, MethodDefinition handler) {
			return removeResolveHandlerCode(blocks, handler, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)");
		}

		public static bool removeAssemblyHookCode(Blocks blocks, MethodDefinition handler) {
			return removeResolveHandlerCode(blocks, handler, "System.Void System.AppDomain::add_AssemblyResolve(System.ResolveEventHandler)");
		}

		static bool removeResolveHandlerCode(Blocks blocks, MethodDefinition handler, string installHandlerMethod) {
			bool modified = false;
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 4; i++) {
					var call = instrs[i];
					if (call.OpCode.Code != Code.Call)
						continue;
					var calledMethod = call.Operand as MethodReference;
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
					var ctor = newobj.Operand as MethodReference;
					if (ctor == null || ctor.FullName != "System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)")
						continue;

					var callvirt = instrs[i + 4];
					if (callvirt.OpCode.Code != Code.Callvirt)
						continue;
					calledMethod = callvirt.Operand as MethodReference;
					if (calledMethod == null || calledMethod.FullName != installHandlerMethod)
						continue;

					block.remove(i, 5);
					modified = true;
				}
			}
			return modified;
		}

		public static byte[] decryptCompressedInt32Data(Arg64ConstantsReader constReader, int exprStart, int exprEnd, BinaryReader reader, byte[] decrypted) {
			for (int i = 0; i < decrypted.Length; i++) {
				constReader.Arg = Utils.readEncodedInt32(reader);
				int index = exprStart;
				long result;
				if (!constReader.getInt64(ref index, out result) || index != exprEnd)
					throw new ApplicationException("Could not decrypt integer");
				decrypted[i] = (byte)result;
			}
			return decrypted;
		}

		public static byte[] decrypt(uint seed, byte[] encrypted) {
			var decrypted = new byte[encrypted.Length];
			ushort _m = (ushort)(seed >> 16);
			ushort _c = (ushort)seed;
			ushort m = _c; ushort c = _m;
			for (int i = 0; i < decrypted.Length; i++) {
				decrypted[i] = (byte)(encrypted[i] ^ (seed * m + c));
				m = (ushort)(seed * m + _m);
				c = (ushort)(seed * c + _c);
			}
			return decrypted;
		}
	}
}
