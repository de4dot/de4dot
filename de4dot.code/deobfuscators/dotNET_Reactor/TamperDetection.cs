/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class TamperDetection {
		TypeDefinition decrypterType;
		MethodDefinition tamperMethod;

		public TamperDetection(TypeDefinition decrypterType) {
			this.decrypterType = decrypterType;
			find();
		}

		public void find() {
			if (decrypterType == null)
				return;

			var requiredTypes = new string[] {
				"System.Byte[]",
				"System.IO.MemoryStream",
				"System.Security.Cryptography.CryptoStream",
				"System.Security.Cryptography.MD5",
				"System.Security.Cryptography.Rijndael",
			};

			foreach (var method in decrypterType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (method.Parameters.Count != 2)
					continue;
				if (!checkType(method.MethodReturnType.ReturnType.FullName, "System.String"))
					continue;
				if (!checkType(method.Parameters[0].ParameterType.FullName, "System.String"))
					continue;
				if (!checkType(method.Parameters[1].ParameterType.FullName, "System.String"))
					continue;

				var localTypes = new LocalTypes(method);
				if (!localTypes.all(requiredTypes))
					continue;

				tamperMethod = method;
				break;
			}
		}

		static bool checkType(string type, string expectedType) {
			return type == "System.Object" || type == expectedType;
		}

		public bool remove(Blocks blocks) {
			if (tamperMethod == null)
				return false;

			Block tamperBlock;
			int numInstructions;
			if (!findBlock(blocks, out tamperBlock, out numInstructions))
				return false;

			if (tamperBlock.FallThrough == null || tamperBlock.Targets == null || tamperBlock.Targets.Count != 1)
				throw new ApplicationException("Invalid state");

			var goodBlock = tamperBlock.Targets[0];
			var badBlock = tamperBlock.FallThrough;

			tamperBlock.replaceLastInstrsWithBranch(numInstructions, goodBlock);

			if (badBlock.FallThrough != badBlock || badBlock.Sources.Count != 1 || badBlock.Targets != null)
				throw new ApplicationException("Invalid state");
			((ScopeBlock)badBlock.Parent).removeGuaranteedDeadBlock(badBlock);

			return true;
		}

		bool findBlock(Blocks blocks, out Block foundBlock, out int numInstructions) {
			const int NUM_INSTRS = 11;

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				if (!block.LastInstr.isBrfalse())
					continue;

				var instructions = block.Instructions;
				if (instructions.Count < NUM_INSTRS)
					continue;
				int i = instructions.Count - NUM_INSTRS;
				if (instructions[i].OpCode.Code != Code.Ldtoken)
					continue;
				if (!(instructions[i].Operand is TypeReference))
					continue;
				if (!checkCall(instructions[i + 1], "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)"))
					continue;
				if (!checkCall(instructions[i + 2], "System.Reflection.Assembly System.Type::get_Assembly()"))
					continue;
				if (!checkCall(instructions[i + 3], "System.Reflection.AssemblyName System.Reflection.Assembly::GetName()"))
					continue;
				if (!checkCall(instructions[i + 4], "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()"))
					continue;
				if (!checkCall(instructions[i + 5], "System.String System.Convert::ToBase64String(System.Byte[])"))
					continue;
				if (instructions[i + 6].OpCode.Code != Code.Ldstr)
					continue;
				if (!checkCall(instructions[i + 7], tamperMethod))
					continue;
				if (instructions[i + 8].OpCode.Code != Code.Ldstr)
					continue;
				if (!checkCall(instructions[i + 9], "System.Boolean System.String::op_Inequality(System.String,System.String)"))
					continue;

				numInstructions = NUM_INSTRS;
				foundBlock = block;
				return true;
			}

			foundBlock = null;
			numInstructions = 0;
			return false;
		}

		static bool checkCall(Instr instr, string methodFullName) {
			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			var calledMethod = instr.Operand as MethodReference;
			if (calledMethod == null)
				return false;
			return calledMethod.FullName == methodFullName;
		}

		static bool checkCall(Instr instr, MethodReference expectedMethod) {
			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			var calledMethod = instr.Operand as MethodReference;
			if (calledMethod == null)
				return false;
			return MemberReferenceHelper.compareMethodReferenceAndDeclaringType(calledMethod, expectedMethod);
		}
	}
}
