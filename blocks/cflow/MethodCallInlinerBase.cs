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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks.cflow {
	public abstract class MethodCallInlinerBase : IBlocksDeobfuscator {
		// We can't catch all infinite loops, so inline methods at most this many times
		const int MAX_ITERATIONS = 10;

		protected Blocks blocks;
		protected Block block;
		int iteration;

		public bool ExecuteOnNoChange { get; set; }

		public void deobfuscateBegin(Blocks blocks) {
			this.blocks = blocks;
			iteration = 0;
		}

		public bool deobfuscate(List<Block> allBlocks) {
			if (iteration++ >= MAX_ITERATIONS)
				return false;

			bool changed = false;
			foreach (var block in allBlocks) {
				this.block = block;
				changed |= deobfuscateInternal();
			}
			return changed;
		}

		protected abstract bool deobfuscateInternal();

		protected class InstructionPatcher {
			readonly int patchIndex;
			public readonly int afterIndex;
			public readonly Instruction lastInstr;
			readonly Instr clonedInstr;
			public InstructionPatcher(int patchIndex, int afterIndex, Instruction lastInstr) {
				this.patchIndex = patchIndex;
				this.afterIndex = afterIndex;
				this.lastInstr = lastInstr;
				this.clonedInstr = new Instr(DotNetUtils.clone(lastInstr));
			}

			public void patch(Block block) {
				block.Instructions[patchIndex] = clonedInstr;
			}
		}

		protected bool inlineLoadMethod(int patchIndex, MethodDefinition methodToInline, Instruction loadInstr, int instrIndex) {
			if (!isReturn(methodToInline, instrIndex))
				return false;

			int methodArgsCount = DotNetUtils.getArgsCount(methodToInline);
			for (int i = 0; i < methodArgsCount; i++)
				block.insert(patchIndex++, Instruction.Create(OpCodes.Pop));

			block.Instructions[patchIndex] = new Instr(DotNetUtils.clone(loadInstr));
			return true;
		}

		protected bool inlineOtherMethod(int patchIndex, MethodDefinition methodToInline, Instruction instr, int instrIndex) {
			return inlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, 0);
		}

		protected bool inlineOtherMethod(int patchIndex, MethodDefinition methodToInline, Instruction instr, int instrIndex, int popLastArgs) {
			return patchMethod(methodToInline, tryInlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, popLastArgs));
		}

		protected bool patchMethod(MethodDefinition methodToInline, InstructionPatcher patcher) {
			if (patcher == null)
				return false;

			if (!isReturn(methodToInline, patcher.afterIndex))
				return false;

			patcher.patch(block);
			return true;
		}

		protected InstructionPatcher tryInlineOtherMethod(int patchIndex, MethodDefinition methodToInline, Instruction instr, int instrIndex) {
			return tryInlineOtherMethod(patchIndex, methodToInline, instr, instrIndex, 0);
		}

		protected InstructionPatcher tryInlineOtherMethod(int patchIndex, MethodDefinition methodToInline, Instruction instr, int instrIndex, int popLastArgs) {
			int loadIndex = 0;
			int methodArgsCount = DotNetUtils.getArgsCount(methodToInline);
			bool foundLdarga = false;
			while (instr != null && loadIndex < methodArgsCount) {
				bool isLdarg = true;
				switch (instr.OpCode.Code) {
				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					break;
				case Code.Ldarga:
				case Code.Ldarga_S:
					foundLdarga = true;
					break;
				default:
					isLdarg = false;
					break;
				}
				if (!isLdarg)
					break;

				if (DotNetUtils.getArgIndex(instr) != loadIndex)
					return null;
				loadIndex++;
				instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
			}
			if (instr == null || loadIndex != methodArgsCount - popLastArgs)
				return null;

			if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
				if (foundLdarga)
					return null;
				var callInstr = instr;
				var calledMethod = callInstr.Operand as MethodReference;
				if (calledMethod == null)
					return null;

				if (!isCompatibleType(-1, calledMethod.MethodReturnType.ReturnType, methodToInline.MethodReturnType.ReturnType))
					return null;

				if (!checkSameMethods(calledMethod, methodToInline, popLastArgs))
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, callInstr);
			}
			else if (instr.OpCode.Code == Code.Newobj) {
				if (foundLdarga)
					return null;
				var newobjInstr = instr;
				var ctor = newobjInstr.Operand as MethodReference;
				if (ctor == null)
					return null;

				if (!isCompatibleType(-1, ctor.DeclaringType, methodToInline.MethodReturnType.ReturnType))
					return null;

				var methodArgs = DotNetUtils.getArgs(methodToInline);
				var calledMethodArgs = DotNetUtils.getArgs(ctor);
				if (methodArgs.Count + 1 - popLastArgs != calledMethodArgs.Count)
					return null;
				for (int i = 1; i < calledMethodArgs.Count; i++) {
					if (!isCompatibleType(i, calledMethodArgs[i], methodArgs[i - 1]))
						return null;
				}

				return new InstructionPatcher(patchIndex, instrIndex, newobjInstr);
			}
			else if (instr.OpCode.Code == Code.Ldfld || instr.OpCode.Code == Code.Ldflda ||
					instr.OpCode.Code == Code.Ldftn || instr.OpCode.Code == Code.Ldvirtftn) {
				var ldInstr = instr;
				if (methodArgsCount != 1)
					return null;

				return new InstructionPatcher(patchIndex, instrIndex, ldInstr);
			}

			return null;
		}

		protected virtual bool isReturn(MethodDefinition methodToInline, int instrIndex) {
			var instr = DotNetUtils.getInstruction(methodToInline.Body.Instructions, ref instrIndex);
			return instr != null && instr.OpCode.Code == Code.Ret;
		}

		protected bool checkSameMethods(MethodReference method, MethodDefinition methodToInline) {
			return checkSameMethods(method, methodToInline, 0);
		}

		protected bool checkSameMethods(MethodReference method, MethodDefinition methodToInline, int ignoreLastMethodToInlineArgs) {
			var methodToInlineArgs = DotNetUtils.getArgs(methodToInline);
			var methodArgs = DotNetUtils.getArgs(method);
			if (methodToInlineArgs.Count - ignoreLastMethodToInlineArgs != methodArgs.Count)
				return false;
			for (int i = 0; i < methodArgs.Count; i++) {
				var methodArg = methodArgs[i];
				var methodToInlineArg = methodToInlineArgs[i];
				if (!isCompatibleType(i, methodArg, methodToInlineArg)) {
					if (i != 0 || !method.HasImplicitThis)
						return false;
					if (!isCompatibleValueThisPtr(methodArg, methodToInlineArg))
						return false;
				}
			}

			return true;
		}

		protected virtual bool isCompatibleType(int paramIndex, TypeReference origType, TypeReference newType) {
			return MemberReferenceHelper.compareTypes(origType, newType);
		}

		static bool isCompatibleValueThisPtr(TypeReference origType, TypeReference newType) {
			var newByRef = newType as ByReferenceType;
			if (newByRef == null)
				return false;
			if (!newByRef.ElementType.IsValueType || !origType.IsValueType)
				return false;
			return MemberReferenceHelper.compareTypes(origType, newByRef.ElementType);
		}
	}
}
