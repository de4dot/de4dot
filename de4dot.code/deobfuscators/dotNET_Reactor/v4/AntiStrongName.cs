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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class AntiStrongName {
		TypeDef decrypterType;
		MethodDef antiStrongNameMethod;

		public AntiStrongName(TypeDef decrypterType) {
			this.decrypterType = decrypterType;
			Find();
		}

		public void Find() {
			if (decrypterType == null)
				return;

			if (CheckType(decrypterType))
				return;

			foreach (var type in decrypterType.NestedTypes) {
				if (CheckType(type))
					return;
			}
		}

		bool CheckType(TypeDef type) {
			var requiredTypes = new string[] {
				"System.Byte[]",
				"System.IO.MemoryStream",
				"System.Security.Cryptography.CryptoStream",
				"System.Security.Cryptography.MD5",
				"System.Security.Cryptography.Rijndael",
			};

			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				var sig = method.MethodSig;
				if (sig == null || sig.Params.Count != 2)
					continue;
				if (!CheckType(sig.RetType, ElementType.String))
					continue;
				if (!CheckType(sig.Params[0], ElementType.String))
					continue;
				if (!CheckType(sig.Params[1], ElementType.String))
					continue;

				var localTypes = new LocalTypes(method);
				if (!localTypes.All(requiredTypes))
					continue;

				antiStrongNameMethod = method;
				return true;
			}

			return false;
		}

		static bool CheckType(TypeSig type, ElementType expectedType) {
			return type != null && (type.ElementType == ElementType.Object || type.ElementType == expectedType);
		}

		public bool Remove(Blocks blocks) {
			if (antiStrongNameMethod == null)
				return false;

			Block antiSnBlock;
			int numInstructions;
			if (!FindBlock(blocks, out antiSnBlock, out numInstructions))
				return false;

			if (antiSnBlock.FallThrough == null || antiSnBlock.Targets == null || antiSnBlock.Targets.Count != 1)
				throw new ApplicationException("Invalid state");

			var goodBlock = antiSnBlock.Targets[0];
			var badBlock = antiSnBlock.FallThrough;

			antiSnBlock.ReplaceLastInstrsWithBranch(numInstructions, goodBlock);

			if (badBlock.FallThrough == badBlock && badBlock.Sources.Count == 1 && badBlock.Targets == null) {
				badBlock.Parent.RemoveGuaranteedDeadBlock(badBlock);
				return true;
			}
			if (badBlock.Instructions.Count <= 1 && badBlock.LastInstr.OpCode.Code == Code.Nop) {
				if (badBlock.FallThrough != null && badBlock.Targets == null && badBlock.Sources.Count == 0) {
					var badBlock2 = badBlock.FallThrough;
					if (badBlock2.FallThrough == badBlock2 && badBlock2.Sources.Count == 2 && badBlock2.Targets == null) {
						badBlock.Parent.RemoveGuaranteedDeadBlock(badBlock);
						badBlock2.Parent.RemoveGuaranteedDeadBlock(badBlock2);
						return true;
					}
				}
			}

			throw new ApplicationException("Invalid state");
		}

		bool FindBlock(Blocks blocks, out Block foundBlock, out int numInstructions) {
			const int NUM_INSTRS = 11;

			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				if (!block.LastInstr.IsBrfalse())
					continue;

				var instructions = block.Instructions;
				if (instructions.Count < NUM_INSTRS)
					continue;
				int i = instructions.Count - NUM_INSTRS;
				if (instructions[i].OpCode.Code != Code.Ldtoken)
					continue;
				if (!(instructions[i].Operand is ITypeDefOrRef))
					continue;
				if (!CheckCall(instructions[i + 1], "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)"))
					continue;
				if (!CheckCall(instructions[i + 2], "System.Reflection.Assembly System.Type::get_Assembly()"))
					continue;
				if (!CheckCall(instructions[i + 3], "System.Reflection.AssemblyName System.Reflection.Assembly::GetName()"))
					continue;
				if (!CheckCall(instructions[i + 4], "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()"))
					continue;
				if (!CheckCall(instructions[i + 5], "System.String System.Convert::ToBase64String(System.Byte[])"))
					continue;
				if (instructions[i + 6].OpCode.Code != Code.Ldstr)
					continue;
				if (!CheckCall(instructions[i + 7], antiStrongNameMethod))
					continue;
				if (instructions[i + 8].OpCode.Code != Code.Ldstr)
					continue;
				if (!CheckCall(instructions[i + 9], "System.Boolean System.String::op_Inequality(System.String,System.String)"))
					continue;

				numInstructions = NUM_INSTRS;
				foundBlock = block;
				return true;
			}

			foundBlock = null;
			numInstructions = 0;
			return false;
		}

		static bool CheckCall(Instr instr, string methodFullName) {
			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			var calledMethod = instr.Operand as IMethod;
			if (calledMethod == null)
				return false;
			return calledMethod.FullName == methodFullName;
		}

		static bool CheckCall(Instr instr, IMethod expectedMethod) {
			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			var calledMethod = instr.Operand as IMethod;
			if (calledMethod == null)
				return false;
			return MethodEqualityComparer.CompareDeclaringTypes.Equals(calledMethod, expectedMethod);
		}
	}
}
